
namespace UnoVibe.Models;

record SessionId(string Id)
{
    public static implicit operator string(SessionId id) => id.Id;
    public override int GetHashCode() => Id.GetHashCode();
    public override string ToString() => Id;
}