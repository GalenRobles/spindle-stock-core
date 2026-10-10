-- =========================================================
-- HACKATHON: ALMACEN INTELIGENTE PARA TALLER
-- Motor: PostgreSQL
-- Ejecutar dentro de la base de datos almacen_inteligente
-- =========================================================

BEGIN;

-- =========================================================
-- 1. CATALOGO DE PIEZAS
-- Los IDs vienen del sistema externo del taller.
-- SKU puede ser NULL o repetirse: NO hacerlo UNIQUE.
-- =========================================================

CREATE TABLE IF NOT EXISTS parts (
    part_id         BIGINT PRIMARY KEY,
    sku             VARCHAR(150),
    name            VARCHAR(250) NOT NULL,
    family          VARCHAR(100),
    part_group      VARCHAR(100),
    subgroup        VARCHAR(100),
    unit            VARCHAR(30) NOT NULL,
    image_url       TEXT,
    active          BOOLEAN NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_parts_sku_normalized
    ON parts (LOWER(BTRIM(sku)));

CREATE INDEX IF NOT EXISTS idx_parts_active
    ON parts (active);

-- =========================================================
-- 2. UBICACIONES DEL ALMACEN
-- Ejemplos: U-100, U-101, U-102...
-- =========================================================

CREATE TABLE IF NOT EXISTS locations (
    location_id     BIGINT PRIMARY KEY,
    code            VARCHAR(20) NOT NULL UNIQUE,
    name            VARCHAR(150) NOT NULL,
    is_workbench    BOOLEAN NOT NULL DEFAULT FALSE,
    active          BOOLEAN NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- =========================================================
-- 3. MODELOS DE EQUIPO
-- =========================================================

CREATE TABLE IF NOT EXISTS equipment_models (
    model_id        BIGINT PRIMARY KEY,
    name            VARCHAR(200) NOT NULL,
    brand           VARCHAR(100) NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- =========================================================
-- 4. LISTA DE MATERIALES (BOM)
-- Una línea puede no tener pieza identificada.
-- qty_per_unit puede ser NULL según el contrato.
-- =========================================================

CREATE TABLE IF NOT EXISTS bom_lines (
    bom_line_id     BIGINT PRIMARY KEY,
    model_id        BIGINT NOT NULL
                    REFERENCES equipment_models(model_id),
    part_id         BIGINT REFERENCES parts(part_id),
    name            VARCHAR(250) NOT NULL,
    group_name      VARCHAR(150),
    unit            VARCHAR(30) NOT NULL,
    qty_per_unit    INTEGER CHECK (qty_per_unit IS NULL
                                   OR qty_per_unit >= 0),
    sort_order      INTEGER NOT NULL DEFAULT 0,
    action          VARCHAR(20) NOT NULL DEFAULT 'buy'
                    CHECK (action IN ('buy', 'repair'))
);

CREATE INDEX IF NOT EXISTS idx_bom_lines_model
    ON bom_lines(model_id);

CREATE INDEX IF NOT EXISTS idx_bom_lines_part
    ON bom_lines(part_id);

-- =========================================================
-- 5. ORDENES DE TRABAJO
-- El código es la clave de negocio que llega en los eventos.
-- =========================================================

CREATE TABLE IF NOT EXISTS work_orders (
    work_order_id   BIGINT PRIMARY KEY,
    code            VARCHAR(50) NOT NULL UNIQUE,
    model_id        BIGINT NOT NULL
                    REFERENCES equipment_models(model_id),
    department      VARCHAR(100) NOT NULL,
    stage           VARCHAR(50) NOT NULL DEFAULT 'received',
    received_at     TIMESTAMPTZ NOT NULL,
    is_deleted      BOOLEAN NOT NULL DEFAULT FALSE,
    deleted_at      TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_work_orders_stage
    ON work_orders(stage);

CREATE INDEX IF NOT EXISTS idx_work_orders_active
    ON work_orders(is_deleted, code);

-- =========================================================
-- 6. INSPECCIONES
-- =========================================================

CREATE TABLE IF NOT EXISTS inspections (
    inspection_id   BIGINT PRIMARY KEY,
    work_order_id   BIGINT NOT NULL
                    REFERENCES work_orders(work_order_id),
    kind            VARCHAR(20) NOT NULL
                    CHECK (kind IN ('quick', 'full')),
    status          VARCHAR(20) NOT NULL
                    CHECK (status IN (
                        'submitted', 'approved', 'rejected',
                        'discarded', 'voided'
                    )),
    approved_by     BIGINT,
    approved_at     TIMESTAMPTZ,
    occurred_at     TIMESTAMPTZ NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_inspections_work_order
    ON inspections(work_order_id, occurred_at);

CREATE TABLE IF NOT EXISTS inspection_items (
    inspection_item_id BIGINT GENERATED ALWAYS AS IDENTITY
                        PRIMARY KEY,
    inspection_id   BIGINT NOT NULL
                    REFERENCES inspections(inspection_id)
                    ON DELETE CASCADE,
    bom_line_id     BIGINT NOT NULL
                    REFERENCES bom_lines(bom_line_id),
    part_id         BIGINT REFERENCES parts(part_id),
    quantity        INTEGER CHECK (quantity IS NULL OR quantity >= 0),
    action          VARCHAR(20) NOT NULL
                    CHECK (action IN ('buy', 'repair')),
    condition       VARCHAR(30),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    -- Evita repetir una misma línea dentro de una inspección.
    UNIQUE (inspection_id, bom_line_id)
);

CREATE INDEX IF NOT EXISTS idx_inspection_items_bom_line
    ON inspection_items(bom_line_id);

-- =========================================================
-- 7. NECESIDAD VIGENTE POR ORDEN Y LINEA BOM
-- La aprobación posterior actualiza la necesidad existente;
-- no debe crear una necesidad acumulada por cada aprobación.
-- =========================================================

CREATE TABLE IF NOT EXISTS work_order_needs (
    need_id         BIGINT GENERATED ALWAYS AS IDENTITY
                    PRIMARY KEY,
    work_order_id   BIGINT NOT NULL
                    REFERENCES work_orders(work_order_id),
    bom_line_id     BIGINT NOT NULL
                    REFERENCES bom_lines(bom_line_id),
    part_id         BIGINT REFERENCES parts(part_id),
    inspection_id   BIGINT NOT NULL
                    REFERENCES inspections(inspection_id),
    required_qty    INTEGER CHECK (
                        required_qty IS NULL OR required_qty >= 0
                    ),
    status          VARCHAR(20) NOT NULL DEFAULT 'active'
                    CHECK (status IN ('active', 'voided', 'closed')),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    UNIQUE (work_order_id, bom_line_id)
);

CREATE INDEX IF NOT EXISTS idx_needs_active_part
    ON work_order_needs(part_id, status);

CREATE INDEX IF NOT EXISTS idx_needs_work_order
    ON work_order_needs(work_order_id, status);

-- =========================================================
-- 8. EXISTENCIAS POR PIEZA Y UBICACION
-- on_hand: existencia física.
-- reserved: cantidad apartada para órdenes.
-- available = on_hand - reserved.
-- Los conteos pueden producir existencias disponibles negativas.
-- =========================================================

CREATE TABLE IF NOT EXISTS inventory_balances (
    part_id         BIGINT NOT NULL REFERENCES parts(part_id),
    location_id     BIGINT NOT NULL REFERENCES locations(location_id),
    on_hand         INTEGER NOT NULL DEFAULT 0,
    reserved        INTEGER NOT NULL DEFAULT 0 CHECK (reserved >= 0),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    PRIMARY KEY (part_id, location_id)
);

CREATE INDEX IF NOT EXISTS idx_balances_location
    ON inventory_balances(location_id);

-- =========================================================
-- 9. RESERVAS PARA ORDENES
-- =========================================================

CREATE TABLE IF NOT EXISTS reservations (
    reservation_id  BIGINT GENERATED ALWAYS AS IDENTITY
                    PRIMARY KEY,
    work_order_id   BIGINT NOT NULL
                    REFERENCES work_orders(work_order_id),
    bom_line_id     BIGINT NOT NULL
                    REFERENCES bom_lines(bom_line_id),
    part_id         BIGINT NOT NULL REFERENCES parts(part_id),
    location_id     BIGINT NOT NULL REFERENCES locations(location_id),
    quantity        INTEGER NOT NULL CHECK (quantity > 0),
    fulfilled_qty   INTEGER NOT NULL DEFAULT 0
                    CHECK (fulfilled_qty >= 0),
    status          VARCHAR(20) NOT NULL DEFAULT 'active'
                    CHECK (status IN (
                        'active', 'fulfilled', 'released'
                    )),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CHECK (fulfilled_qty <= quantity)
);

CREATE INDEX IF NOT EXISTS idx_reservations_work_order
    ON reservations(work_order_id, status);

CREATE INDEX IF NOT EXISTS idx_reservations_part
    ON reservations(part_id, location_id, status);

-- =========================================================
-- 10. FALTANTES DE INVENTARIO
-- part_id y missing_quantity admiten NULL para casos
-- donde la pieza o su cantidad no pueden determinarse.
-- =========================================================

CREATE TABLE IF NOT EXISTS shortages (
    shortage_id     BIGINT GENERATED ALWAYS AS IDENTITY
                    PRIMARY KEY,
    work_order_id   BIGINT NOT NULL
                    REFERENCES work_orders(work_order_id),
    bom_line_id     BIGINT NOT NULL
                    REFERENCES bom_lines(bom_line_id),
    part_id         BIGINT REFERENCES parts(part_id),
    inspection_id   BIGINT NOT NULL
                    REFERENCES inspections(inspection_id),
    missing_quantity INTEGER CHECK (
                        missing_quantity IS NULL
                        OR missing_quantity >= 0
                    ),
    status          VARCHAR(20) NOT NULL DEFAULT 'open'
                    CHECK (status IN ('open', 'resolved', 'closed')),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    resolved_at     TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_shortages_open_part
    ON shortages(part_id, created_at)
    WHERE status = 'open';

CREATE INDEX IF NOT EXISTS idx_shortages_work_order
    ON shortages(work_order_id, status);

-- =========================================================
-- 11. RECEPCIONES DE COMPRAS
-- La misma solicitud puede contener varias líneas.
-- =========================================================

CREATE TABLE IF NOT EXISTS purchase_receipts (
    line_id             BIGINT PRIMARY KEY,
    purchase_request_id BIGINT NOT NULL,
    request_number      VARCHAR(100) NOT NULL,
    part_number         VARCHAR(150) NOT NULL,
    description         VARCHAR(250) NOT NULL,
    quantity            INTEGER NOT NULL CHECK (quantity > 0),
    unit_price          NUMERIC(14,2) NOT NULL CHECK (unit_price >= 0),
    currency            VARCHAR(3) NOT NULL,
    purpose             VARCHAR(30) NOT NULL
                        CHECK (purpose IN ('restock', 'customer_order')),
    received_at         TIMESTAMPTZ NOT NULL,
    work_order_code     VARCHAR(50),
    matched_part_id     BIGINT REFERENCES parts(part_id),
    location_id         BIGINT REFERENCES locations(location_id),
    status              VARCHAR(25) NOT NULL DEFAULT 'pending'
                        CHECK (status IN (
                            'pending', 'received', 'unmatched',
                            'ignored_customer_order', 'resolved'
                        )),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_receipts_request
    ON purchase_receipts(request_number);

CREATE INDEX IF NOT EXISTS idx_receipts_part_number
    ON purchase_receipts(LOWER(BTRIM(part_number)));

CREATE INDEX IF NOT EXISTS idx_receipts_status
    ON purchase_receipts(status);

-- =========================================================
-- 12. SUGERENCIAS DE REABASTECIMIENTO
-- Una sugerencia vigente por pieza.
-- =========================================================

CREATE TABLE IF NOT EXISTS reorder_suggestions (
    suggestion_id       BIGINT GENERATED ALWAYS AS IDENTITY
                        PRIMARY KEY,
    part_id             BIGINT NOT NULL REFERENCES parts(part_id),
    suggested_quantity  INTEGER NOT NULL
                        CHECK (suggested_quantity >= 0),
    status              VARCHAR(20) NOT NULL DEFAULT 'active'
                        CHECK (status IN (
                            'active', 'ordered', 'fulfilled', 'cancelled'
                        )),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_reorder_active_part
    ON reorder_suggestions(part_id)
    WHERE status = 'active';

CREATE TABLE IF NOT EXISTS reorder_suggestion_work_orders (
    suggestion_id   BIGINT NOT NULL
                    REFERENCES reorder_suggestions(suggestion_id)
                    ON DELETE CASCADE,
    work_order_id   BIGINT NOT NULL
                    REFERENCES work_orders(work_order_id),
    PRIMARY KEY (suggestion_id, work_order_id)
);

-- =========================================================
-- 13. MOVIMIENTOS DE INVENTARIO: AUDITORIA
-- Registrar cada entrada, salida, transferencia o ajuste.
-- =========================================================

CREATE TABLE IF NOT EXISTS inventory_movements (
    movement_id     BIGINT GENERATED ALWAYS AS IDENTITY
                    PRIMARY KEY,
    part_id         BIGINT NOT NULL REFERENCES parts(part_id),
    from_location_id BIGINT REFERENCES locations(location_id),
    to_location_id  BIGINT REFERENCES locations(location_id),
    quantity        INTEGER NOT NULL CHECK (quantity > 0),
    movement_type   VARCHAR(25) NOT NULL
                    CHECK (movement_type IN (
                        'received', 'issued', 'transferred',
                        'adjusted', 'reserved', 'released'
                    )),
    reason          TEXT,
    reference_type  VARCHAR(50),
    reference_id    VARCHAR(100),
    occurred_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_movements_part_date
    ON inventory_movements(part_id, occurred_at DESC);

CREATE INDEX IF NOT EXISTS idx_movements_reference
    ON inventory_movements(reference_type, reference_id);

-- =========================================================
-- 14. EVENTOS PROCESADOS: IDEMPOTENCIA
-- El event_id es UUID y no debe procesarse dos veces.
-- =========================================================

CREATE TABLE IF NOT EXISTS processed_events (
    event_id        UUID PRIMARY KEY,
    event_type      VARCHAR(100) NOT NULL,
    occurred_at     TIMESTAMPTZ NOT NULL,
    processed_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    result          VARCHAR(20) NOT NULL DEFAULT 'processed'
                    CHECK (result IN (
                        'processed', 'ignored', 'pending', 'failed'
                    ))
);

CREATE INDEX IF NOT EXISTS idx_processed_events_date
    ON processed_events(processed_at);

-- =========================================================
-- 15. EVENTOS PENDIENTES
-- Para eventos que llegan antes que sus piezas, órdenes o BOM.
-- =========================================================

CREATE TABLE IF NOT EXISTS pending_events (
    pending_id      BIGINT GENERATED ALWAYS AS IDENTITY
                    PRIMARY KEY,
    event_id        UUID NOT NULL UNIQUE,
    event_type      VARCHAR(100) NOT NULL,
    event_key       VARCHAR(200) NOT NULL,
    payload         JSONB NOT NULL,
    missing_entity  VARCHAR(50),
    attempts        INTEGER NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    next_retry_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    status          VARCHAR(20) NOT NULL DEFAULT 'pending'
                    CHECK (status IN ('pending', 'processed', 'failed')),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    processed_at    TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_pending_retry
    ON pending_events(status, next_retry_at);

-- =========================================================
-- 16. DEAD LETTER QUEUE (DLQ)
-- Conservar el evento original si no pudo procesarse.
-- =========================================================

CREATE TABLE IF NOT EXISTS dead_letter_events (
    dead_letter_id  BIGINT GENERATED ALWAYS AS IDENTITY
                    PRIMARY KEY,
    event_id        UUID NOT NULL UNIQUE,
    event_type      VARCHAR(100) NOT NULL,
    event_key       VARCHAR(200) NOT NULL,
    payload         JSONB NOT NULL,
    error_message   TEXT NOT NULL,
    attempts        INTEGER NOT NULL DEFAULT 0,
    failed_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    resolved        BOOLEAN NOT NULL DEFAULT FALSE,
    resolved_at     TIMESTAMPTZ
);

-- =========================================================
-- 17. OUTBOX DE EVENTOS DE SALIDA
-- Guarda eventos por publicar en inventory.events.
-- Permite persistir el evento junto con los cambios de BD.
-- =========================================================

CREATE TABLE IF NOT EXISTS outbox_events (
    outbox_id       BIGINT GENERATED ALWAYS AS IDENTITY
                    PRIMARY KEY,
    event_id        UUID NOT NULL UNIQUE,
    event_type      VARCHAR(100) NOT NULL,
    event_version   INTEGER NOT NULL DEFAULT 1,
    occurred_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    event_key       VARCHAR(200) NOT NULL,
    payload         JSONB NOT NULL,
    published       BOOLEAN NOT NULL DEFAULT FALSE,
    published_at    TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_outbox_unpublished
    ON outbox_events(created_at)
    WHERE published = FALSE;

-- =========================================================
-- 18. CONTEOS DE INVENTARIO
-- El motivo es obligatorio; la API debe devolver 422 si falta.
-- =========================================================

CREATE TABLE IF NOT EXISTS inventory_counts (
    count_id        BIGINT GENERATED ALWAYS AS IDENTITY
                    PRIMARY KEY,
    part_id         BIGINT NOT NULL REFERENCES parts(part_id),
    location_id     BIGINT NOT NULL REFERENCES locations(location_id),
    expected_qty    INTEGER NOT NULL,
    counted_qty     INTEGER NOT NULL,
    difference_qty  INTEGER GENERATED ALWAYS AS
                    (counted_qty - expected_qty) STORED,
    reason          TEXT NOT NULL CHECK (LENGTH(BTRIM(reason)) > 0),
    counted_by      VARCHAR(100),
    counted_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_counts_part_location
    ON inventory_counts(part_id, location_id, counted_at DESC);

-- =========================================================
-- 19. RECEPCIONES SIN COINCIDENCIA: RESOLUCION MANUAL
-- La recepción se conserva hasta que una persona seleccione
-- la pieza correcta del catálogo.
-- =========================================================

CREATE TABLE IF NOT EXISTS unmatched_receipts (
    unmatched_receipt_id BIGINT GENERATED ALWAYS AS IDENTITY
                         PRIMARY KEY,
    line_id              BIGINT NOT NULL UNIQUE
                         REFERENCES purchase_receipts(line_id),
    part_number          VARCHAR(150) NOT NULL,
    candidate_part_ids   BIGINT[] NOT NULL DEFAULT '{}',
    reason               VARCHAR(30) NOT NULL
                         CHECK (reason IN ('not_found', 'ambiguous')),
    status               VARCHAR(20) NOT NULL DEFAULT 'open'
                         CHECK (status IN ('open', 'resolved')),
    resolved_part_id     BIGINT REFERENCES parts(part_id),
    created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    resolved_at          TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_unmatched_open
    ON unmatched_receipts(status, created_at);

COMMIT;

-- =========================================================
-- FIN DEL SCRIPT
-- =========================================================