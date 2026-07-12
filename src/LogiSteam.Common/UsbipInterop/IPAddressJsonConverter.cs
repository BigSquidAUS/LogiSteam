using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogiSteam.Common.UsbipInterop;

public sealed class IPAddressJsonConverter : JsonConverter<IPAddress?>
{
    public override IPAddress? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var text = reader.GetString();
        return string.IsNullOrEmpty(text) ? null : IPAddress.Parse(text);
    }

    public override void Write(Utf8JsonWriter writer, IPAddress? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
