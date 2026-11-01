using System.Numerics;

namespace GroundWar.Sim.Units;

/// <summary>One sighting of an enemy: where and when he was last seen. Not where he is now.</summary>
public sealed class Contact
{
    public int EnemyId { get; init; }
    public Vector2 LastPosition { get; set; }
    public float LastSeen { get; set; }
    public float Confidence { get; set; }
    public bool IsMachineGun { get; set; }
}

/// <summary>What one side knows about the other. Only filled in by its soldiers' eyes and ears.
/// (Milestone 2 shares it instantly across the side; milestone 3 adds proper fog of war and reporting.)</summary>
public sealed class SideKnowledge
{
    public Side Side { get; }
    private readonly Dictionary<int, Contact> _contacts = new();
    public IEnumerable<Contact> Contacts => _contacts.Values;
    public int Count => _contacts.Count;

    public SideKnowledge(Side side) { Side = side; }

    public void Report(Soldier enemy, Vector2 position, float time, float confidence)
    {
        if (!_contacts.TryGetValue(enemy.Id, out var c))
        {
            c = new Contact { EnemyId = enemy.Id };
            _contacts[enemy.Id] = c;
        }
        // A confident sighting overrides a guess; an old fact is replaced by any new one.
        if (confidence >= c.Confidence || time - c.LastSeen > 2f)
        {
            c.LastPosition = position;
            c.Confidence = confidence;
        }
        c.LastSeen = time;
        c.IsMachineGun |= enemy.Weapon.IsMachineGun;
    }

    public Contact? Get(int enemyId) => _contacts.TryGetValue(enemyId, out var c) ? c : null;

    public bool SeenRecently(int enemyId, float now, float window) =>
        _contacts.TryGetValue(enemyId, out var c) && now - c.LastSeen <= window && c.Confidence >= 0.9f;

    public void Forget(int enemyId) => _contacts.Remove(enemyId);

    public void Expire(float now, float memory)
    {
        List<int>? old = null;
        foreach (var (id, c) in _contacts)
            if (now - c.LastSeen > memory) (old ??= new()).Add(id);
        if (old != null) foreach (var id in old) _contacts.Remove(id);
    }
}
