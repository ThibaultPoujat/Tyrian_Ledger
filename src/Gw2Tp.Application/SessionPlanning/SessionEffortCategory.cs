namespace Gw2Tp.Application.SessionPlanning;

/// <summary>
/// A coarse, source-supplied planning label. It deliberately does not estimate execution duration.
/// </summary>
public enum SessionEffortCategory
{
    VeryLow,
    Low,
    Medium,
    High,
    OngoingPatient,
}
