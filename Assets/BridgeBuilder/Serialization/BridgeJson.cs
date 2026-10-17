using BridgeBuilder.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BridgeBuilder.Serialization
{
    /// <summary>JSON for saved bridges and test fixtures (spec section 3). Never used in the simulation loop.</summary>
    public static class BridgeJson
    {
        static readonly JsonSerializerSettings Options = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = { new StringEnumConverter() },
        };

        public static string ToJson(BridgeDesign design) => JsonConvert.SerializeObject(design.ToData(), Options);

        public static BridgeDesign FromJson(BridgeSettings settings, string json) =>
            BridgeDesign.FromData(settings, JsonConvert.DeserializeObject<BridgeDesignData>(json, Options));
    }
}
