namespace Nodey.Core.Graph;

/// <summary>
/// A connection from an output port to an input port.
/// </summary>
/// <remarks>
/// The spec's skeleton is a two-field record (SourcePortId, TargetPortId);
/// the <see cref="Id"/> is an addition required by the spec's own
/// <c>Disconnect(Guid connectionId)</c> API — the UI (STEP-4) needs a stable
/// per-connection identity to select and disconnect specific connections.
/// Save/load stores explicit node ids + port indices (not port ids), so the
/// connection id is regenerated on load.
/// </remarks>
public sealed record GraphConnection(Guid Id, Guid SourcePortId, Guid TargetPortId);
