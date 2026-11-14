using UnityEngine;
namespace SprocketUiKit;

public enum GraphHitKind { Canvas, Node, Port, Wire, FooterButton }
public readonly struct GraphHit
{
    public GraphHitKind Kind { get; init; }
    public string? NodeId { get; init; }
    public string? PortId { get; init; }
    public string? WireId { get; init; }
    public Vector2 GraphPos { get; init; }
    public int Button { get; init; }
    public bool Shift { get; init; }
    public bool Ctrl { get; init; }
}
/// <summary>A completed port drag. Drop uses the same z-ordered picking as clicks.</summary>
public readonly struct GraphPortDrop
{
    public string FromNode { get; }
    public string FromPort { get; }
    public bool FromOutput { get; }
    public GraphHit Drop { get; }
    public GraphPortDrop(string fromNode, string fromPort, bool fromOutput, GraphHit drop)
    { FromNode=fromNode; FromPort=fromPort; FromOutput=fromOutput; Drop=drop; }
}
