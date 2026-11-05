using System.Diagnostics.CodeAnalysis;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// SEC-018: <c>envio.pdf_copia</c> se guarda cifrada. Las pruebas que comparan «la copia exacta»
/// leen las filas por estos ayudantes, que devuelven la entidad (desconectada) con el PDF en claro;
/// la columna cruda se comprueba aparte, en <see cref="EnvioCopiaCifradaTests"/>.
/// </summary>
public static class CopiaEnvio
{
    public static ServicioCifrado Cifrado { get; } = new(new OpcionesSeguridad
    {
        ClaveJwt = new byte[64],
        ClaveCifrado = ApiTestFactory.ClaveCifrado,
    });

    [return: NotNullIfNotNull(nameof(envio))]
    public static Envio? EnClaro(Envio? envio)
    {
        if (envio is not null)
        {
            envio.PdfCopia = Cifrado.DescifrarBytes(envio.PdfCopia);
        }

        return envio;
    }

    public static List<Envio> EnClaro(List<Envio> envios)
    {
        envios.ForEach(e => EnClaro(e));
        return envios;
    }
}
