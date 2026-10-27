using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// SEC-030: ningún decimal de una petición puede pasar de lo que cabe en <c>numeric(18,2)</c>
/// (16 enteros y 2 decimales). Sin tope, un importe enorme llegaba a la base de datos y reventaba
/// con un 500; aquí se rechaza en la entrada y la validación de modelo responde 400.
/// <para>
/// Es un conversor global porque el tope es el mismo para todo el dinero del sistema y repetir un
/// <c>[Range]</c> en cada DTO deja siempre alguno sin poner. Los tipos de cambio (<c>numeric(18,6)</c>)
/// y los porcentajes ya tienen su <c>[Range]</c>, más estricto, y este tope solo es la red de abajo.
/// </para>
/// </summary>
public sealed class ImporteJsonConverter : JsonConverter<decimal>
{
    /// <summary>El mayor valor que cabe en <c>numeric(18,2)</c>.</summary>
    public const decimal Maximo = 9_999_999_999_999_999.99m;

    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // El frontend manda el dinero como texto decimal (AGENTS §6) y las opciones web admiten
        // números entre comillas: hay que leer los dos, o toda la entrada de importes da 400.
        var valor = reader.TokenType == JsonTokenType.String
            && decimal.TryParse(
                reader.GetString(),
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var leido)
            ? leido
            : reader.GetDecimal();
        if (Math.Abs(valor) > Maximo)
        {
            throw new JsonException($"El importe no puede pasar de {Maximo:0.00} en valor absoluto.");
        }

        return valor;
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}
