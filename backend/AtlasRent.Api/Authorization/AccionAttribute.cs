namespace AtlasRent.Api.Authorization;

/// <summary>
/// Declara qué acción exige un endpoint. Sin este atributo el endpoint se rechaza (política global
/// de denegar por omisión), porque un endpoint sin declarar es un endpoint abierto por descuido.
/// <para>
/// Un endpoint con más de una acción exige las dos.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AccionAttribute(params string[] acciones) : Attribute
{
    /// <summary>Acciones exigidas. Vacia significa que el endpoint no declara ninguna.</summary>
    public string[] Acciones { get; } = acciones;
}