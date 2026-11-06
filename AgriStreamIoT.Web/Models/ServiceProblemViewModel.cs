namespace AgriStreamIoT.Web.Models;

/// <summary>Shown when an Azure dependency is missing or unavailable.</summary>
public sealed record ServiceProblemViewModel(string Service, string Message, string Hint, bool IsConfiguration, string Setting);
