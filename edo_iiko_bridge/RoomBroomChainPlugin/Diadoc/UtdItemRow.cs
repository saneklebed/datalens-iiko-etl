using System;

namespace RoomBroomChainPlugin.Diadoc
{
    /// <summary>
    /// Упрощённая строка УПД для отображения и выгрузки в iiko.
    /// </summary>
    public class UtdItemRow
    {
        public int LineIndex { get; set; }
        public string Product { get; set; }
        /// <summary>Наименование товара у поставщика (отдельная колонка в гриде).</summary>
        public string SupplierProductName { get; set; }
        public string Unit { get; set; }
        public string UnitName { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Vat { get; set; }
        public string ItemVendorCode { get; set; }
        public string ItemArticle { get; set; }
        public string Gtin { get; set; }
        public string ItemAdditionalInfo { get; set; }
        /// <summary>Наименование товара в нашей системе (iiko), если найдена привязка по прайс-листу.</summary>
        public string IikoProductName { get; set; }
    }
}

