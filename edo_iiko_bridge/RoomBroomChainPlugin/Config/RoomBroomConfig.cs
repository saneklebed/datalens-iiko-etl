using System.Runtime.Serialization;

namespace RoomBroomChainPlugin.Config
{
    [DataContract]
    public class RoomBroomConfig
    {
        [DataMember(Order = 1)]
        public string DiadocLogin { get; set; } = "";

        [DataMember(Order = 2)]
        public string DiadocPassword { get; set; } = "";

        [DataMember(Order = 3)]
        public string DiadocApiToken { get; set; } = "";

        // Backward compatible with earlier saved configs (old JSON key: NeedRecipientSignature)
        [DataMember(Name = "NeedRecipientSignature", Order = 4)]
        public bool CreateInvoiceWithPosting { get; set; } = false;

        [DataMember(Order = 5)]
        public bool EnableReports { get; set; } = false;
    }
}

