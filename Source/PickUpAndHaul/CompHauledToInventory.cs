namespace PickUpAndHaul;

public class CompHauledToInventory : ThingComp
{
	private HashSet<Thing> takenToInventory = new();

	public HashSet<Thing> GetHashSet()
	{
		TrimList();
		return takenToInventory;
	}

	public void RegisterHauledItem(Thing thing)
	{
		if (thing.Destroyed || thing.stackCount == 0)
		{
			return;
		}

		takenToInventory.Add(thing);
	}

	public void UnregisterHauledItem(Thing thing)
	{
		if (thing == null)
		{
			return;
		}

		TrimList();
		takenToInventory.Remove(thing);
	}

	public bool IsEmpty() => takenToInventory.Count == 0;

	public bool Contains(Thing thing) => takenToInventory.Contains(thing);
	public void Clear() => takenToInventory.Clear();

	private void TrimList()
	{
		// todo check `x.destroyed` ?
		takenToInventory.RemoveWhere(x => x == null || x.stackCount == 0);
	}

	public override void PostExposeData()
	{
		base.PostExposeData();
		TrimList();
		Scribe_Collections.Look(ref takenToInventory, "ThingsHauledToInventory", LookMode.Reference);
	}
}
