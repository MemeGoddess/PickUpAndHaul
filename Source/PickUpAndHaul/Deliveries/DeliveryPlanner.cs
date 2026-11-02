using System.Linq;

namespace PickUpAndHaul.Deliveries;

public class DeliveryPlanner(Pawn pawn)
{
	public Pawn pawn = pawn;
	public Dictionary<WorkGiver_HaulToInventory.StoreTarget, WorkGiver_HaulToInventory.CellAllocation> storeCellCapacity = new();
	public List<WorkGiver_HaulToInventory.CellAllocation> pickupList = new();
	public List<WorkGiver_HaulToInventory.StoreTarget> deliverList = new();

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
