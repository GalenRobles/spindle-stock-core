using System;
using System.Collections.Generic;
using AlmacenTaller.Models;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.DataContext;

public partial class AppDbContext : DbContext
{
    public AppDbContext()
    {
    }

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<BomLine> BomLines { get; set; }

    public virtual DbSet<DeadLetterEvent> DeadLetterEvents { get; set; }

    public virtual DbSet<EquipmentModel> EquipmentModels { get; set; }

    public virtual DbSet<Inspection> Inspections { get; set; }

    public virtual DbSet<InspectionItem> InspectionItems { get; set; }

    public virtual DbSet<InventoryBalance> InventoryBalances { get; set; }

    public virtual DbSet<InventoryCount> InventoryCounts { get; set; }

    public virtual DbSet<InventoryMovement> InventoryMovements { get; set; }

    public virtual DbSet<Location> Locations { get; set; }

    public virtual DbSet<NeedSource> NeedSources { get; set; }

    public virtual DbSet<OutboxEvent> OutboxEvents { get; set; }

    public virtual DbSet<Parts> Parts { get; set; }

    public virtual DbSet<PartPolicy> PartPolicies { get; set; }

    public virtual DbSet<PendingEvent> PendingEvents { get; set; }

    public virtual DbSet<ProcessedEvent> ProcessedEvents { get; set; }

    public virtual DbSet<PurchaseReceipt> PurchaseReceipts { get; set; }

    public virtual DbSet<ReorderSuggestion> ReorderSuggestions { get; set; }

    public virtual DbSet<Reservation> Reservations { get; set; }

    public virtual DbSet<Shortage> Shortages { get; set; }

    public virtual DbSet<StockMovement> StockMovements { get; set; }

    public virtual DbSet<UnmatchedReceipt> UnmatchedReceipts { get; set; }

    public virtual DbSet<VPartLedger> VPartLedgers { get; set; }

    public virtual DbSet<VWorkOrderMaterial> VWorkOrderMaterials { get; set; }

    public virtual DbSet<WorkOrder> WorkOrders { get; set; }

    public virtual DbSet<WorkOrderNeed> WorkOrderNeeds { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {

    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BomLine>(entity =>
        {
            entity.HasKey(e => e.BomLineId).HasName("bom_lines_pkey");

            entity.HasIndex(e => e.ModelId, "idx_bom_lines_model").HasFilter("(removed_at IS NULL)");

            entity.Property(e => e.BomLineId).ValueGeneratedNever();
            entity.Property(e => e.KitKey).HasComputedColumnSql("\nCASE\n    WHEN (group_name IS NOT NULL) THEN ('g:'::text || (group_name)::text)\n    ELSE ('n:'::text || (name)::text)\nEND", true);
            entity.Property(e => e.SortOrder).HasDefaultValue(0);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.Model).WithMany(p => p.BomLines)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("bom_lines_model_id_fkey");
        });

        modelBuilder.Entity<DeadLetterEvent>(entity =>
        {
            entity.HasKey(e => e.DeadLetterId).HasName("dead_letter_events_pkey");

            entity.Property(e => e.DeadLetterId).UseIdentityAlwaysColumn();
            entity.Property(e => e.Attempts).HasDefaultValue(0);
            entity.Property(e => e.FailedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.Resolved).HasDefaultValue(false);
        });

        modelBuilder.Entity<EquipmentModel>(entity =>
        {
            entity.HasKey(e => e.ModelId).HasName("equipment_models_pkey");

            entity.Property(e => e.ModelId).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<Inspection>(entity =>
        {
            entity.HasKey(e => e.InspectionId).HasName("inspections_pkey");

            entity.Property(e => e.InspectionId).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<InspectionItem>(entity =>
        {
            entity.HasKey(e => e.InspectionItemId).HasName("inspection_items_pkey");

            entity.Property(e => e.InspectionItemId).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.Inspection).WithMany(p => p.InspectionItems).HasConstraintName("inspection_items_inspection_id_fkey");
        });

        modelBuilder.Entity<InventoryBalance>(entity =>
        {
            entity.HasKey(e => new { e.PartId, e.LocationId }).HasName("inventory_balances_pkey");

            entity.Property(e => e.Available).HasComputedColumnSql("(on_hand - reserved)", true);
            entity.Property(e => e.OnHand).HasDefaultValue(0);
            entity.Property(e => e.Reserved).HasDefaultValue(0);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.Location).WithMany(p => p.InventoryBalances)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("inventory_balances_location_id_fkey");

            entity.HasOne(d => d.Part).WithMany(p => p.InventoryBalances)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("inventory_balances_part_id_fkey");
        });

        modelBuilder.Entity<InventoryCount>(entity =>
        {
            entity.HasKey(e => e.CountId).HasName("inventory_counts_pkey");

            entity.Property(e => e.CountId).UseIdentityAlwaysColumn();
            entity.Property(e => e.CountedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.DifferenceQty).HasComputedColumnSql("(counted_qty - expected_qty)", true);

            entity.HasOne(d => d.Location).WithMany(p => p.InventoryCounts)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("inventory_counts_location_id_fkey");

            entity.HasOne(d => d.Movement).WithMany(p => p.InventoryCounts).HasConstraintName("inventory_counts_movement_id_fkey");

            entity.HasOne(d => d.Part).WithMany(p => p.InventoryCounts)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("inventory_counts_part_id_fkey");
        });

        modelBuilder.Entity<InventoryMovement>(entity =>
        {
            entity.HasKey(e => e.MovementId).HasName("inventory_movements_pkey");

            entity.HasIndex(e => e.WorkOrderId, "idx_movements_work_order").HasFilter("(work_order_id IS NOT NULL)");

            entity.Property(e => e.MovementId).UseIdentityAlwaysColumn();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.OccurredAt).HasDefaultValueSql("now()");
            entity.Property(e => e.OnHandDelta).HasDefaultValue(0);
            entity.Property(e => e.ReservedDelta).HasDefaultValue(0);

            entity.HasOne(d => d.Location).WithMany(p => p.InventoryMovements)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("inventory_movements_location_id_fkey");

            entity.HasOne(d => d.Part).WithMany(p => p.InventoryMovements)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("inventory_movements_part_id_fkey");
        });

        modelBuilder.Entity<Location>(entity =>
        {
            entity.HasKey(e => e.LocationId).HasName("locations_pkey");

            entity.Property(e => e.LocationId).ValueGeneratedNever();
            entity.Property(e => e.Active).HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.IsWorkbench).HasDefaultValue(false);
        });

        modelBuilder.Entity<NeedSource>(entity =>
        {
            entity.HasKey(e => new { e.NeedId, e.InspectionItemId }).HasName("need_sources_pkey");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.Need).WithMany(p => p.NeedSources).HasConstraintName("need_sources_need_id_fkey");
        });

        modelBuilder.Entity<OutboxEvent>(entity =>
        {
            entity.HasKey(e => e.OutboxId).HasName("outbox_events_pkey");

            entity.HasIndex(e => e.CreatedAt, "idx_outbox_unpublished").HasFilter("(published = false)");

            entity.Property(e => e.OutboxId).UseIdentityAlwaysColumn();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.EventVersion).HasDefaultValue(1);
            entity.Property(e => e.OccurredAt).HasDefaultValueSql("now()");
            entity.Property(e => e.Published).HasDefaultValue(false);
        });

        modelBuilder.Entity<Parts>(entity =>
        {
            entity.HasKey(e => e.PartId).HasName("parts_pkey");

            entity.Property(e => e.PartId).ValueGeneratedNever();
            entity.Property(e => e.Active).HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.SkuNorm).HasComputedColumnSql("NULLIF(lower(regexp_replace((sku)::text, '\\s+'::text, ''::text, 'g'::text)), ''::text)", true);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<PartPolicy>(entity =>
        {
            entity.HasKey(e => e.PartId).HasName("part_policies_pkey");

            entity.Property(e => e.PartId).ValueGeneratedNever();
            entity.Property(e => e.MinQty).HasDefaultValue(0);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.Part).WithOne(p => p.PartPolicy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("part_policies_part_id_fkey");
        });

        modelBuilder.Entity<PendingEvent>(entity =>
        {
            entity.HasKey(e => e.PendingId).HasName("pending_events_pkey");

            entity.Property(e => e.PendingId).UseIdentityAlwaysColumn();
            entity.Property(e => e.Attempts).HasDefaultValue(0);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.NextRetryAt).HasDefaultValueSql("now()");
            entity.Property(e => e.Status).HasDefaultValueSql("'pending'::character varying");
        });

        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.HasKey(e => e.EventId).HasName("processed_events_pkey");

            entity.Property(e => e.EventId).ValueGeneratedNever();
            entity.Property(e => e.ProcessedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.Result).HasDefaultValueSql("'processed'::character varying");
        });

        modelBuilder.Entity<PurchaseReceipt>(entity =>
        {
            entity.HasKey(e => e.LineId).HasName("purchase_receipts_pkey");

            entity.Property(e => e.LineId).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.PartNumberNorm).HasComputedColumnSql("lower(regexp_replace((part_number)::text, '\\s+'::text, ''::text, 'g'::text))", true);
            entity.Property(e => e.Status).HasDefaultValueSql("'pending'::character varying");

            entity.HasOne(d => d.Location).WithMany(p => p.PurchaseReceipts).HasConstraintName("purchase_receipts_location_id_fkey");

            entity.HasOne(d => d.MatchedPart).WithMany(p => p.PurchaseReceipts).HasConstraintName("purchase_receipts_matched_part_id_fkey");
        });

        modelBuilder.Entity<ReorderSuggestion>(entity =>
        {
            entity.HasKey(e => e.SuggestionId).HasName("reorder_suggestions_pkey");

            entity.HasIndex(e => e.PartId, "uq_reorder_active_part")
                .IsUnique()
                .HasFilter("((status)::text = 'active'::text)");

            entity.Property(e => e.SuggestionId).UseIdentityAlwaysColumn();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.Status).HasDefaultValueSql("'active'::character varying");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.Part).WithOne(p => p.ReorderSuggestion)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("reorder_suggestions_part_id_fkey");

            entity.HasMany(d => d.WorkOrders).WithMany(p => p.Suggestions)
                .UsingEntity<Dictionary<string, object>>(
                    "ReorderSuggestionWorkOrder",
                    r => r.HasOne<WorkOrder>().WithMany()
                        .HasForeignKey("WorkOrderId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("reorder_suggestion_work_orders_work_order_id_fkey"),
                    l => l.HasOne<ReorderSuggestion>().WithMany()
                        .HasForeignKey("SuggestionId")
                        .HasConstraintName("reorder_suggestion_work_orders_suggestion_id_fkey"),
                    j =>
                    {
                        j.HasKey("SuggestionId", "WorkOrderId").HasName("reorder_suggestion_work_orders_pkey");
                        j.ToTable("reorder_suggestion_work_orders");
                        j.IndexerProperty<long>("SuggestionId").HasColumnName("suggestion_id");
                        j.IndexerProperty<long>("WorkOrderId").HasColumnName("work_order_id");
                    });
        });

        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.HasKey(e => e.ReservationId).HasName("reservations_pkey");

            entity.HasIndex(e => new { e.WorkOrderId, e.BomLineId, e.LocationId }, "uq_reservation_active")
                .IsUnique()
                .HasFilter("((status)::text = 'active'::text)");

            entity.Property(e => e.ReservationId).UseIdentityAlwaysColumn();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.FulfilledQty).HasDefaultValue(0);
            entity.Property(e => e.Status).HasDefaultValueSql("'active'::character varying");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.Inspection).WithMany(p => p.Reservations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("reservations_inspection_id_fkey");

            entity.HasOne(d => d.InspectionItem).WithMany(p => p.Reservations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("reservations_inspection_item_id_fkey");

            entity.HasOne(d => d.Location).WithMany(p => p.Reservations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("reservations_location_id_fkey");

            entity.HasOne(d => d.Part).WithMany(p => p.Reservations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("reservations_part_id_fkey");

            entity.HasOne(d => d.WorkOrder).WithMany(p => p.Reservations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("reservations_work_order_id_fkey");
        });

        modelBuilder.Entity<Shortage>(entity =>
        {
            entity.HasKey(e => e.ShortageId).HasName("shortages_pkey");

            entity.HasIndex(e => new { e.PartId, e.CreatedAt }, "idx_shortages_open_part").HasFilter("((status)::text = 'open'::text)");

            entity.HasIndex(e => new { e.WorkOrderId, e.BomLineId }, "uq_shortage_open")
                .IsUnique()
                .HasFilter("((status)::text = 'open'::text)");

            entity.Property(e => e.ShortageId).UseIdentityAlwaysColumn();
            entity.Property(e => e.CoveredQuantity).HasDefaultValue(0);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.Status).HasDefaultValueSql("'open'::character varying");

            entity.HasOne(d => d.WorkOrder).WithMany(p => p.Shortages)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("shortages_work_order_id_fkey");
        });

        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.HasKey(e => e.MovementId).HasName("stock_movements_pkey");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UnitCost).HasDefaultValueSql("0.0000");
        });

        modelBuilder.Entity<UnmatchedReceipt>(entity =>
        {
            entity.HasKey(e => e.UnmatchedReceiptId).HasName("unmatched_receipts_pkey");

            entity.Property(e => e.UnmatchedReceiptId).UseIdentityAlwaysColumn();
            entity.Property(e => e.CandidatePartIds).HasDefaultValueSql("'{}'::bigint[]");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.Status).HasDefaultValueSql("'open'::character varying");

            entity.HasOne(d => d.Line).WithOne(p => p.UnmatchedReceipt)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("unmatched_receipts_line_id_fkey");

            entity.HasOne(d => d.ResolvedPart).WithMany(p => p.UnmatchedReceipts).HasConstraintName("unmatched_receipts_resolved_part_id_fkey");
        });

        modelBuilder.Entity<VPartLedger>(entity =>
        {
            entity.ToView("v_part_ledger");
        });

        modelBuilder.Entity<VWorkOrderMaterial>(entity =>
        {
            entity.ToView("v_work_order_materials");
        });

        modelBuilder.Entity<WorkOrder>(entity =>
        {
            entity.HasKey(e => e.WorkOrderId).HasName("work_orders_pkey");

            entity.Property(e => e.WorkOrderId).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.IsDeleted).HasDefaultValue(false);
            entity.Property(e => e.Stage).HasDefaultValueSql("'awaiting_quick_inspection'::character varying");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<WorkOrderNeed>(entity =>
        {
            entity.HasKey(e => e.NeedId).HasName("work_order_needs_pkey");

            entity.Property(e => e.NeedId).UseIdentityAlwaysColumn();
            entity.Property(e => e.Status).HasDefaultValueSql("'active'::character varying");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.WorkOrder).WithMany(p => p.WorkOrderNeeds)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("work_order_needs_work_order_id_fkey");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
