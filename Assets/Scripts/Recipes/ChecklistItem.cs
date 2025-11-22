public enum ChecklistKind
{
    TeaType,  // Added: Validate which tea type is in the cup
    Fill,
    Water,
    Dips,
    Milk,
    Sugar,
    Ice,
    Powder,
    Topping
}

public sealed class ChecklistItem
{
    public ChecklistKind Kind { get; }
    public string Id { get; }
    public float Target { get; }
    public float Current { get; }
    public bool Required { get; }
    public bool Complete { get; }

    public ChecklistItem(ChecklistKind kind, string id, float target, float current, bool required, bool complete)
    {
        Kind = kind;
        Id = id;
        Target = target;
        Current = current;
        Required = required;
        Complete = complete;
    }
}