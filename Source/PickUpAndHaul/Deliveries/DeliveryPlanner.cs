using System.Linq;
using System.Text;

namespace PickUpAndHaul.Deliveries;

public class DeliveryPlanner(Pawn pawn)
{
	public Pawn pawn = pawn;
	public Dictionary<WorkGiver_HaulToInventory.StoreTarget, WorkGiver_HaulToInventory.CellAllocation> storeCellCapacity = new();
	public List<WorkGiver_HaulToInventory.CellAllocation> pickupList = new();
	public List<WorkGiver_HaulToInventory.StoreTarget> deliverList = new();


	public void SortPawnMovements(IntVec3 startPosition)
	{
		// sorted delivery/pickups, this reduces pawn dancing and reduces delivery times
		pickupList = SortPawnMovementList(pawn, startPosition, pickupList);
		// the delivery list only needs to be sorted once JobDriver_HaulToInventory uses the delivery list
		//deliverList = SortPawnMovementList(pickupList.Last().allocated.Position, deliverList);
	}

	private List<T> SortPawnMovementList<T>(Pawn pawn, IntVec3 position, List<T> initialList) where T : notnull, IPositionable
	{
		var outputList = new List<T>(initialList.Count);
		if (initialList.Count == 0)
		{
			return outputList;
		}

		// avoid destructively mutating the input list
		var workingList = new List<T>(initialList);
		PositionableComparer<T> pawnMovementComparer = new();

		var s = new StringBuilder();
		s.Append($"{position},");
		//While this is O(n * n * log(n)), it should be relatively efficient as these lists *should* be reasonable lengths
		while (workingList.Count > 0)
		{
			pawnMovementComparer.rootCell = position;
			workingList.Sort(pawnMovementComparer);

			var closestItem = workingList[^1];
			workingList.RemoveAt(workingList.Count - 1);
			position = closestItem.Position;
			outputList.Add(closestItem);
			s.Append($"{position},");
		}

		Log.Message($"{pawn} moving on path: {s} for pickups");

		return outputList;
	}

	public Job MakeJob()
	{
		var job = JobMaker.MakeJob(JobDefOfs.HaulToInventory, null, deliverList.FirstOrDefault() );   //Things will be in queues
		job.targetQueueA = new List<LocalTargetInfo>(pickupList.Count); //more things
		job.targetQueueB = new List<LocalTargetInfo>(deliverList.Count - 1); //more storage; keep in mind the job doesn't use it, but reserve it so you don't over-haul
		job.countQueue = new List<int>(pickupList.Count);//thing counts

		foreach (var valueTuple in pickupList)
		{
			job.targetQueueA.Add(valueTuple.allocated);
			job.countQueue.Add(valueTuple.capacity);
		}
		foreach (var valueTuple in deliverList.Skip(1))
		{
			job.targetQueueB.Add(valueTuple);
		}

		return job;
	}
}
