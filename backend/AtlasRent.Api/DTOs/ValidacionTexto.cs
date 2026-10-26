using System.ComponentModel.DataAnnotations;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Correo de un cliente, contacto o envío (SEC-028). <see cref="EmailAddressAttribute"/> solo pide
/// una «@» en medio, así que dejaba pasar <c>a@b.com\r\nBcc: x@y.z</c>: un salto de línea en un
/// destinatario es inyección de cabeceras. Aquí además no se admiten caracteres de control, espacios
/// ni separadores de lista. Un valor vacío o nulo es válido (los campos son opcionales; la
/// obligatoriedad la declara <c>[Required]</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class EmailSeguroAttribute : ValidationAttribute
{
    private static readonly EmailAddressAttribute Formato = new();

    public static bool EsValido(string? valor) =>
        string.IsNullOrEmpty(valor)
        || (!valor.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is ',' or ';' or '<' or '>')
            && Formato.IsValid(valor));

    public override bool IsValid(object? value) => value is null || (value is string s && EsValido(s));

    public override string FormatErrorMessage(string name) => $"{name} no es un correo válido.";
}

/// <summary>
/// Texto de una sola línea (nombres, asuntos): sin CR, LF ni otros caracteres de control (SEC-028).
/// Van a cabeceras de correo, a PDF y a logs, donde un salto de línea cambia el significado.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class UnaLineaAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) =>
        value is null || (value is string s && !s.Any(char.IsControl));

    public override string FormatErrorMessage(string name) => $"{name} no admite saltos de línea ni caracteres de control.";
}
