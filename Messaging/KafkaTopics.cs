namespace AlmacenTaller.Messaging;

/// <summary>Nombres de tópicos según contract/README.md §2.</summary>
public static class KafkaTopics
{
    // Entrada
    public const string ShopCatalog = "shop.catalog";
    public const string ShopWorkOrders = "shop.work_orders";
    public const string ShopInspections = "shop.inspections";
    public const string ShopPurchasing = "shop.purchasing";

    // Salida
    public const string InventoryEvents = "inventory.events";
    public const string InventoryDlq = "inventory.dlq";

    /// <summary>Tópicos que se consumen DESPUÉS de tener el catálogo cargado (regla 22).</summary>
    public static readonly string[] ShopOperational = { ShopWorkOrders, ShopInspections, ShopPurchasing };
}
