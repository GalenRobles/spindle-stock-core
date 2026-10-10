BEGIN;

CREATE TABLE IF NOT EXISTS parts (
    part_id         BIGINT PRIMARY KEY,
    sku             VARCHAR,
    sku_norm        VARCHAR GENERATED ALWAYS AS (
                        NULLIF(LOWER(REGEXP_REPLACE(sku, '\s+', '', 'g')), '')
                    ) STORED,
    name            VARCHAR NOT NULL,
    family          VARCHAR,
    part_group      VARCHAR,
    subgroup        VARCHAR,
    unit_id         BIGINT,
    unit_name       VARCHAR,
    image_url       VARCHAR,
    active          BOOLEAN NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_parts_sku_norm ON parts (sku_norm);
CREATE INDEX IF NOT EXISTS idx_parts_active   ON parts (active);

CREATE TABLE IF NOT EXISTS part_policies (
    part_id         BIGINT PRIMARY KEY REFERENCES parts(part_id),
    min_qty         INTEGER NOT NULL DEFAULT 0 CHECK (min_qty >= 0),
    max_qty         INTEGER CHECK (max_qty IS NULL OR max_qty >= min_qty),
    reorder_qty     INTEGER CHECK (reorder_qty IS NULL OR reorder_qty > 0),
    lead_time_days  INTEGER CHECK (lead_time_days IS NULL OR lead_time_days >= 0),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS locations (
    location_id     BIGINT PRIMARY KEY,
    code            VARCHAR(20) NOT NULL UNIQUE,
    name            VARCHAR NOT NULL,
    is_workbench    BOOLEAN NOT NULL DEFAULT FALSE,
    active          BOOLEAN NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS equipment_models (
    model_id        BIGINT PRIMARY KEY,
    name            VARCHAR NOT NULL,
    brand           VARCHAR NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS bom_lines (
    bom_line_id     BIGINT PRIMARY KEY,
    model_id        BIGINT NOT NULL REFERENCES equipment_models(model_id),
    part_id         BIGINT,
    name            VARCHAR NOT NULL,
    group_name      VARCHAR,
    unit_id         BIGINT,
    unit_name       VARCHAR,
    qty_per_unit    INTEGER CHECK (qty_per_unit IS NULL OR qty_per_unit >= 0),
    sort_order      INTEGER NOT NULL DEFAULT 0,
    removed_at      TIMESTAMPTZ,
    kit_key         VARCHAR GENERATED ALWAYS AS (
                        CASE WHEN group_name IS NOT NULL
                             THEN 'g:' || group_name
                             ELSE 'n:' || name
                        END
                    ) STORED,
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_bom_lines_model ON bom_lines(model_id) WHERE removed_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_bom_lines_part  ON bom_lines(part_id);
CREATE INDEX IF NOT EXISTS idx_bom_lines_kit   ON bom_lines(model_id, kit_key);

CREATE TABLE IF NOT EXISTS work_orders (
    work_order_id   BIGINT PRIMARY KEY,
    code            VARCHAR(50) NOT NULL UNIQUE,
    model_id        BIGINT NOT NULL,
    department      VARCHAR NOT NULL,
    stage           VARCHAR NOT NULL DEFAULT 'awaiting_quick_inspection',
    received_at     TIMESTAMPTZ NOT NULL,
    is_deleted      BOOLEAN NOT NULL DEFAULT FALSE,
    deleted_at      TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_work_orders_stage  ON work_orders(stage);
CREATE INDEX IF NOT EXISTS idx_work_orders_active ON work_orders(is_deleted, code);

CREATE TABLE IF NOT EXISTS inspections (
    inspection_id   BIGINT PRIMARY KEY,
    work_order_id   BIGINT NOT NULL,
    kind            VARCHAR(20) NOT NULL CHECK (kind IN ('quick', 'full')),
    status          VARCHAR(20) NOT NULL
                    CHECK (status IN ('submitted', 'approved', 'rejected',
                                      'discarded', 'voided')),
    approved_by     BIGINT,
    approved_at     TIMESTAMPTZ,
    voided_at       TIMESTAMPTZ,
    reason          VARCHAR,
    occurred_at     TIMESTAMPTZ NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_inspections_work_order
    ON inspections(work_order_id, occurred_at);

CREATE TABLE IF NOT EXISTS inspection_items (
    inspection_item_id BIGINT PRIMARY KEY,
    inspection_id   BIGINT NOT NULL
                    REFERENCES inspections(inspection_id) ON DELETE CASCADE,
    bom_line_id     BIGINT NOT NULL,
    part_id         BIGINT,
    name            VARCHAR NOT NULL,
    group_name      VARCHAR,
    condition       VARCHAR,
    action          VARCHAR(20) CHECK (action IS NULL OR action IN ('buy', 'repair')),
    quantity        INTEGER CHECK (quantity IS NULL OR quantity >= 0),
    note            VARCHAR,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_inspection_items_inspection ON inspection_items(inspection_id);
CREATE INDEX IF NOT EXISTS idx_inspection_items_bom_line   ON inspection_items(bom_line_id);

CREATE TABLE IF NOT EXISTS work_order_needs (
    need_id         BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    work_order_id   BIGINT NOT NULL REFERENCES work_orders(work_order_id),
    bom_line_id     BIGINT NOT NULL,
    part_id         BIGINT,
    name            VARCHAR NOT NULL,
    inspection_id   BIGINT NOT NULL,
    inspection_item_id BIGINT NOT NULL,
    required_qty    INTEGER CHECK (required_qty IS NULL OR required_qty >= 0),
    status          VARCHAR(20) NOT NULL DEFAULT 'active'
                    CHECK (status IN ('active', 'voided', 'closed')),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    UNIQUE (work_order_id, bom_line_id)
);

CREATE INDEX IF NOT EXISTS idx_needs_active_part ON work_order_needs(part_id, status);
CREATE INDEX IF NOT EXISTS idx_needs_work_order  ON work_order_needs(work_order_id, status);

CREATE TABLE IF NOT EXISTS need_sources (
    need_id         BIGINT NOT NULL REFERENCES work_order_needs(need_id) ON DELETE CASCADE,
    inspection_item_id BIGINT NOT NULL,
    inspection_id   BIGINT NOT NULL,
    required_qty    INTEGER CHECK (required_qty IS NULL OR required_qty >= 0),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (need_id, inspection_item_id)
);

CREATE INDEX IF NOT EXISTS idx_need_sources_inspection ON need_sources(inspection_id);

CREATE TABLE IF NOT EXISTS inventory_balances (
    part_id         BIGINT NOT NULL REFERENCES parts(part_id),
    location_id     BIGINT NOT NULL REFERENCES locations(location_id),
    on_hand         INTEGER NOT NULL DEFAULT 0,
    reserved        INTEGER NOT NULL DEFAULT 0 CHECK (reserved >= 0),
    available       INTEGER GENERATED ALWAYS AS (on_hand - reserved) STORED,
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    PRIMARY KEY (part_id, location_id)
);

CREATE INDEX IF NOT EXISTS idx_balances_location ON inventory_balances(location_id);

CREATE OR REPLACE FUNCTION fn_balance_guard() RETURNS trigger AS $$
BEGIN
    IF (NEW.on_hand - NEW.reserved) < 0
       AND COALESCE(current_setting('app.allow_negative', true), '') <> 'on'
       AND (TG_OP = 'INSERT'
            OR (NEW.on_hand - NEW.reserved) < (OLD.on_hand - OLD.reserved))
    THEN
        RAISE EXCEPTION 'available negativo: parte % ubicacion % (on_hand=%, reserved=%)',
            NEW.part_id, NEW.location_id, NEW.on_hand, NEW.reserved
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END $$ LANGUAGE plpgsql;

CREATE OR REPLACE TRIGGER trg_balance_guard
    BEFORE INSERT OR UPDATE OF on_hand, reserved ON inventory_balances
    FOR EACH ROW EXECUTE FUNCTION fn_balance_guard();

CREATE TABLE IF NOT EXISTS inventory_movements (
    movement_id     BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    part_id         BIGINT NOT NULL REFERENCES parts(part_id),
    location_id     BIGINT NOT NULL REFERENCES locations(location_id),
    movement_type   VARCHAR(20) NOT NULL
                    CHECK (movement_type IN ('receipt', 'issue', 'transfer_out',
                                             'transfer_in', 'adjustment',
                                             'reserve', 'release')),
    on_hand_delta   INTEGER NOT NULL DEFAULT 0,
    reserved_delta  INTEGER NOT NULL DEFAULT 0,
    work_order_id   BIGINT,
    reservation_id  BIGINT,
    purchase_line_id BIGINT,
    transfer_id     UUID,
    actor           VARCHAR,
    reason          VARCHAR,
    source_event_id UUID,
    dedupe_key      VARCHAR UNIQUE,
    occurred_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_movement_shape CHECK (
        (movement_type = 'receipt'      AND on_hand_delta > 0 AND reserved_delta = 0) OR
        (movement_type = 'issue'        AND on_hand_delta < 0 AND reserved_delta <= 0) OR
        (movement_type = 'transfer_out' AND on_hand_delta < 0 AND reserved_delta = 0) OR
        (movement_type = 'transfer_in'  AND on_hand_delta > 0 AND reserved_delta = 0) OR
        (movement_type = 'adjustment'   AND on_hand_delta <> 0 AND reserved_delta = 0) OR
        (movement_type = 'reserve'      AND on_hand_delta = 0 AND reserved_delta > 0) OR
        (movement_type = 'release'      AND on_hand_delta = 0 AND reserved_delta < 0)
    ),
    CONSTRAINT chk_movement_adjust_reason CHECK (
        movement_type <> 'adjustment' OR (reason IS NOT NULL AND BTRIM(reason) <> '')
    ),
    CONSTRAINT chk_movement_issue_ctx CHECK (
        movement_type <> 'issue' OR (work_order_id IS NOT NULL AND actor IS NOT NULL)
    ),
    CONSTRAINT chk_movement_transfer_ctx CHECK (
        movement_type NOT IN ('transfer_out', 'transfer_in') OR transfer_id IS NOT NULL
    )
);

CREATE INDEX IF NOT EXISTS idx_movements_part_date
    ON inventory_movements(part_id, occurred_at, movement_id);
CREATE INDEX IF NOT EXISTS idx_movements_location
    ON inventory_movements(location_id, part_id);
CREATE INDEX IF NOT EXISTS idx_movements_work_order
    ON inventory_movements(work_order_id) WHERE work_order_id IS NOT NULL;

CREATE OR REPLACE FUNCTION fn_movements_immutable() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'inventory_movements es inmutable: corrija con un movimiento nuevo';
END $$ LANGUAGE plpgsql;

CREATE OR REPLACE TRIGGER trg_movements_immutable
    BEFORE UPDATE OR DELETE ON inventory_movements
    FOR EACH ROW EXECUTE FUNCTION fn_movements_immutable();

CREATE OR REPLACE TRIGGER trg_movements_no_truncate
    BEFORE TRUNCATE ON inventory_movements
    FOR EACH STATEMENT EXECUTE FUNCTION fn_movements_immutable();

CREATE OR REPLACE FUNCTION fn_apply_movement() RETURNS trigger AS $$
BEGIN
    UPDATE inventory_balances
       SET on_hand    = on_hand  + NEW.on_hand_delta,
           reserved   = reserved + NEW.reserved_delta,
           updated_at = NOW()
     WHERE part_id = NEW.part_id AND location_id = NEW.location_id;

    IF NOT FOUND THEN
        BEGIN
            INSERT INTO inventory_balances(part_id, location_id, on_hand, reserved)
            VALUES (NEW.part_id, NEW.location_id, NEW.on_hand_delta, NEW.reserved_delta);
        EXCEPTION WHEN unique_violation THEN
            UPDATE inventory_balances
               SET on_hand    = on_hand  + NEW.on_hand_delta,
                   reserved   = reserved + NEW.reserved_delta,
                   updated_at = NOW()
             WHERE part_id = NEW.part_id AND location_id = NEW.location_id;
        END;
    END IF;
    RETURN NULL;
END $$ LANGUAGE plpgsql;

CREATE OR REPLACE TRIGGER trg_apply_movement
    AFTER INSERT ON inventory_movements
    FOR EACH ROW EXECUTE FUNCTION fn_apply_movement();

CREATE OR REPLACE VIEW v_part_ledger AS
SELECT m.*,
       SUM(m.on_hand_delta)  OVER w AS on_hand_running,
       SUM(m.reserved_delta) OVER w AS reserved_running
  FROM inventory_movements m
WINDOW w AS (PARTITION BY m.part_id ORDER BY m.occurred_at, m.movement_id);

CREATE TABLE IF NOT EXISTS reservations (
    reservation_id  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    work_order_id   BIGINT NOT NULL REFERENCES work_orders(work_order_id),
    bom_line_id     BIGINT NOT NULL,
    part_id         BIGINT NOT NULL REFERENCES parts(part_id),
    location_id     BIGINT NOT NULL REFERENCES locations(location_id),
    inspection_id   BIGINT NOT NULL REFERENCES inspections(inspection_id),
    inspection_item_id BIGINT NOT NULL REFERENCES inspection_items(inspection_item_id),
    quantity        INTEGER NOT NULL CHECK (quantity > 0),
    fulfilled_qty   INTEGER NOT NULL DEFAULT 0 CHECK (fulfilled_qty >= 0),
    status          VARCHAR(20) NOT NULL DEFAULT 'active'
                    CHECK (status IN ('active', 'fulfilled', 'cancelled')),
    cancelled_at    TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CHECK (fulfilled_qty <= quantity)
);

CREATE INDEX IF NOT EXISTS idx_reservations_work_order ON reservations(work_order_id, status);
CREATE INDEX IF NOT EXISTS idx_reservations_part       ON reservations(part_id, location_id, status);
CREATE INDEX IF NOT EXISTS idx_reservations_inspection ON reservations(inspection_id, status);

CREATE UNIQUE INDEX IF NOT EXISTS uq_reservation_active
    ON reservations(work_order_id, bom_line_id, location_id)
    WHERE status = 'active';

CREATE TABLE IF NOT EXISTS shortages (
    shortage_id     BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    work_order_id   BIGINT NOT NULL REFERENCES work_orders(work_order_id),
    bom_line_id     BIGINT NOT NULL,
    part_id         BIGINT,
    name            VARCHAR NOT NULL,
    inspection_id   BIGINT NOT NULL,
    inspection_item_id BIGINT NOT NULL,
    missing_quantity INTEGER CHECK (missing_quantity IS NULL OR missing_quantity >= 0),
    covered_quantity INTEGER NOT NULL DEFAULT 0 CHECK (covered_quantity >= 0),
    status          VARCHAR(20) NOT NULL DEFAULT 'open'
                    CHECK (status IN ('open', 'resolved', 'closed')),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    resolved_at     TIMESTAMPTZ,

    CHECK (missing_quantity IS NULL OR covered_quantity <= missing_quantity)
);

CREATE INDEX IF NOT EXISTS idx_shortages_open_part
    ON shortages(part_id, created_at) WHERE status = 'open';
CREATE INDEX IF NOT EXISTS idx_shortages_work_order ON shortages(work_order_id, status);
CREATE INDEX IF NOT EXISTS idx_shortages_inspection ON shortages(inspection_id, status);

CREATE UNIQUE INDEX IF NOT EXISTS uq_shortage_open
    ON shortages(work_order_id, bom_line_id) WHERE status = 'open';

CREATE TABLE IF NOT EXISTS purchase_receipts (
    line_id             BIGINT PRIMARY KEY,
    purchase_request_id BIGINT NOT NULL,
    request_number      VARCHAR NOT NULL,
    part_number         VARCHAR NOT NULL,
    part_number_norm    VARCHAR GENERATED ALWAYS AS (
                            LOWER(REGEXP_REPLACE(part_number, '\s+', '', 'g'))
                        ) STORED,
    description         VARCHAR NOT NULL,
    quantity            INTEGER NOT NULL CHECK (quantity > 0),
    unit_price          NUMERIC(18,6) NOT NULL CHECK (unit_price >= 0),
    currency            VARCHAR(3) NOT NULL,
    purpose             VARCHAR(30) NOT NULL CHECK (purpose IN ('restock', 'customer_order')),
    received_at         TIMESTAMPTZ NOT NULL,
    work_order_code     VARCHAR(50),
    matched_part_id     BIGINT REFERENCES parts(part_id),
    location_id         BIGINT REFERENCES locations(location_id),
    status              VARCHAR(25) NOT NULL DEFAULT 'pending'
                        CHECK (status IN ('pending', 'received', 'unmatched',
                                          'ignored_customer_order', 'resolved')),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_receipt_stocked_complete
        CHECK (status NOT IN ('received', 'resolved')
               OR (matched_part_id IS NOT NULL AND location_id IS NOT NULL)),

    CONSTRAINT chk_receipt_customer_not_stocked
        CHECK (purpose <> 'customer_order'
               OR status IN ('pending', 'ignored_customer_order'))
);

CREATE INDEX IF NOT EXISTS idx_receipts_request     ON purchase_receipts(request_number);
CREATE INDEX IF NOT EXISTS idx_receipts_part_norm   ON purchase_receipts(part_number_norm);
CREATE INDEX IF NOT EXISTS idx_receipts_status      ON purchase_receipts(status);
CREATE INDEX IF NOT EXISTS idx_receipts_work_order  ON purchase_receipts(work_order_code);

CREATE TABLE IF NOT EXISTS unmatched_receipts (
    unmatched_receipt_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    line_id              BIGINT NOT NULL UNIQUE REFERENCES purchase_receipts(line_id),
    part_number          VARCHAR NOT NULL,
    candidate_part_ids   BIGINT[] NOT NULL DEFAULT '{}',
    reason               VARCHAR(30) NOT NULL CHECK (reason IN ('not_found', 'ambiguous')),
    status               VARCHAR(20) NOT NULL DEFAULT 'open'
                         CHECK (status IN ('open', 'resolved')),
    resolved_part_id     BIGINT REFERENCES parts(part_id),
    created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    resolved_at          TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_unmatched_open ON unmatched_receipts(status, created_at);

CREATE TABLE IF NOT EXISTS reorder_suggestions (
    suggestion_id       BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    part_id             BIGINT NOT NULL REFERENCES parts(part_id),
    suggested_quantity  INTEGER NOT NULL CHECK (suggested_quantity >= 0),
    status              VARCHAR(20) NOT NULL DEFAULT 'active'
                        CHECK (status IN ('active', 'ordered', 'fulfilled', 'cancelled')),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_reorder_active_part
    ON reorder_suggestions(part_id) WHERE status = 'active';

CREATE TABLE IF NOT EXISTS reorder_suggestion_work_orders (
    suggestion_id   BIGINT NOT NULL REFERENCES reorder_suggestions(suggestion_id) ON DELETE CASCADE,
    work_order_id   BIGINT NOT NULL REFERENCES work_orders(work_order_id),
    PRIMARY KEY (suggestion_id, work_order_id)
);

CREATE TABLE IF NOT EXISTS inventory_counts (
    count_id        BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    part_id         BIGINT NOT NULL REFERENCES parts(part_id),
    location_id     BIGINT NOT NULL REFERENCES locations(location_id),
    expected_qty    INTEGER NOT NULL,
    counted_qty     INTEGER NOT NULL CHECK (counted_qty >= 0),
    difference_qty  INTEGER GENERATED ALWAYS AS (counted_qty - expected_qty) STORED,
    reason          VARCHAR NOT NULL CHECK (LENGTH(BTRIM(reason)) > 0),
    counted_by      VARCHAR,
    movement_id     BIGINT REFERENCES inventory_movements(movement_id),
    counted_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_counts_part_location
    ON inventory_counts(part_id, location_id, counted_at DESC);

CREATE TABLE IF NOT EXISTS processed_events (
    event_id        UUID PRIMARY KEY,
    event_type      VARCHAR(100) NOT NULL,
    occurred_at     TIMESTAMPTZ NOT NULL,
    processed_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    result          VARCHAR(20) NOT NULL DEFAULT 'processed'
                    CHECK (result IN ('processed', 'ignored', 'pending', 'failed'))
);

CREATE INDEX IF NOT EXISTS idx_processed_events_date ON processed_events(processed_at);

CREATE TABLE IF NOT EXISTS pending_events (
    pending_id      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
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

CREATE INDEX IF NOT EXISTS idx_pending_retry ON pending_events(status, next_retry_at);

CREATE TABLE IF NOT EXISTS dead_letter_events (
    dead_letter_id  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    event_id        UUID NOT NULL UNIQUE,
    event_type      VARCHAR(100) NOT NULL,
    event_key       VARCHAR(200) NOT NULL,
    payload         JSONB NOT NULL,
    error_message   VARCHAR NOT NULL,
    attempts        INTEGER NOT NULL DEFAULT 0,
    failed_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    resolved        BOOLEAN NOT NULL DEFAULT FALSE,
    resolved_at     TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS outbox_events (
    outbox_id       BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
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
    ON outbox_events(created_at) WHERE published = FALSE;

CREATE OR REPLACE VIEW v_work_order_materials AS
SELECT w.code                AS work_order_code,
       n.work_order_id,
       n.need_id,
       n.inspection_item_id,
       n.bom_line_id,
       n.part_id,
       n.name,
       n.required_qty,
       n.status              AS need_status,
       COALESCE(r.reserved_qty, 0)   AS reserved_qty,
       COALESCE(r.delivered_qty, 0)  AS delivered_qty,
       COALESCE(s.pending_qty, 0)    AS shortage_qty,
       COALESCE(s.unknown, FALSE)    AS shortage_unknown
  FROM work_order_needs n
  JOIN work_orders w ON w.work_order_id = n.work_order_id
  LEFT JOIN LATERAL (
        SELECT SUM(x.quantity - x.fulfilled_qty) FILTER (WHERE x.status = 'active') AS reserved_qty,
               SUM(x.fulfilled_qty)              FILTER (WHERE x.status IN ('active', 'fulfilled')) AS delivered_qty
          FROM reservations x
         WHERE x.work_order_id = n.work_order_id AND x.bom_line_id = n.bom_line_id
  ) r ON TRUE
  LEFT JOIN LATERAL (
        SELECT SUM(y.missing_quantity - y.covered_quantity) AS pending_qty,
               BOOL_OR(y.missing_quantity IS NULL OR y.part_id IS NULL) AS unknown
          FROM shortages y
         WHERE y.work_order_id = n.work_order_id AND y.bom_line_id = n.bom_line_id
           AND y.status = 'open'
  ) s ON TRUE;

COMMIT;

CREATE TABLE IF NOT EXISTS processed_events (
    event_id VARCHAR(100) PRIMARY KEY,
    event_type VARCHAR(100) NOT NULL,
    processed_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS inventory_balances (
    part_id INT NOT NULL,
    location_id INT NOT NULL,
    quantity INT NOT NULL DEFAULT 0,
    PRIMARY KEY (part_id, location_id),
    CONSTRAINT chk_positive_quantity CHECK (quantity >= 0)
);

CREATE TABLE IF NOT EXISTS stock_movements (
    movement_id SERIAL PRIMARY KEY,
    event_id VARCHAR(100) UNIQUE,
    part_id INT NOT NULL,
    location_id INT NOT NULL,
    quantity INT NOT NULL,
    unit_cost NUMERIC(12, 4) DEFAULT 0.0000,
    movement_type VARCHAR(50) NOT NULL, 
    reference_id VARCHAR(100),          
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);