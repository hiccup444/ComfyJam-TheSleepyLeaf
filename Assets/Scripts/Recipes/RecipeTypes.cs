using System.Collections.Generic;

public enum WaterSource
{
    None = 0,
    Hot,
    Cold
}

public enum MilkKind
{
    None = 0,
    Dairy,
    Oat
}

public interface ICupState
{
    bool IsFull { get; }
    WaterSource Water { get; }
    int Dips { get; }
    MilkKind Milk { get; }
    bool HasSugar { get; }
    bool HasIce { get; }
    bool HasPowder { get; }
    IReadOnlyCollection<string> Toppings { get; }
    TeaType TeaType { get; } // Added: Which tea is in the cup
}