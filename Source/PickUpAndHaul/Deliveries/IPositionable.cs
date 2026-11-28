namespace PickUpAndHaul.Deliveries;

public interface IPositionable
{
	public IntVec3 Position { get; }
}

public class PositionableComparer<T> : IComparer<T> where T : notnull, IPositionable
{
	public IntVec3 rootCell;
	public int Compare(T x, T y) => (y.Position - rootCell).LengthHorizontalSquared.CompareTo((x.Position - rootCell).LengthHorizontalSquared);
}
