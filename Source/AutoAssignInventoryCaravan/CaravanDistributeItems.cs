using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace AutoAssignInventoryCaravan
{
	public static class CaravanDistributeItems
	{
		public static void DebugLog(string s)
		{
#if DEBUG
			Log.Message(s);
#endif
		}

		public static void DistributeThingsForPoliciesAndCarry(IEnumerable<Pawn> caravanPawns)
		{
			// get all required thingDefs from drug policies and carry assignments for all colonist pawns in the caravan
			var reqTotalAll = new Dictionary<ThingDef, int>();
			var reqCountPawns = new Dictionary<ThingDef, Dictionary<Pawn, int>>();
			foreach (var pawn in caravanPawns)
			{
				// only check colonists for required stuff
				if (pawn.IsColonist)
				{
					// create list of all thingDefs and their count required for this pawn via their drug policy and carry assignment
					foreach (var req in pawn.GetAllRequired())
					{
						var thingDef = req.Key;
						var count = req.Value;

						// remember the count of thingDef required in total and for this pawn
						if (!reqTotalAll.ContainsKey(thingDef))
						{
							reqTotalAll.Add(thingDef, count);
							reqCountPawns.Add(thingDef, new Dictionary<Pawn, int> { { pawn, count } });
						}
						else
						{
							reqTotalAll[thingDef] += count;
							reqCountPawns[thingDef].Add(pawn, count);
						}
					}
				}
			}

			// get all things and their counts for the required thingDefs in the inventories of all pawns in the caravan
			var invTotalAll = new Dictionary<ThingDef, int>();
			var invCountPawns = new Dictionary<ThingDef, Dictionary<Pawn, int>>();
			var inventoriesAll = new Dictionary<ThingDef, Dictionary<Pawn, List<Thing>>>();
			// create dictionaries for required thingDefs
			foreach (var thingDef in reqTotalAll.Keys)
			{
				invTotalAll.Add(thingDef, 0);
				invCountPawns.Add(thingDef, new Dictionary<Pawn, int>());
				inventoriesAll.Add(thingDef, new Dictionary<Pawn, List<Thing>>());
			}
			// check each pawn's inventory for required things
			foreach (var pawn in caravanPawns)
			{
				foreach (var thing in pawn.inventory.innerContainer)
				{
					var thingDef = thing.def;

					// sort out only things that are required
					if (invTotalAll.ContainsKey(thingDef))
					{
						var count = thing.stackCount;

						// remember the total of required thingDef found in all inventories
						invTotalAll[thingDef] += count;

						// remember the count of required thingDef found in this pawn's inventory
						var invCountForPawn = invCountPawns[thingDef];
						var inventoryForPawn = inventoriesAll[thingDef];
						if (!invCountForPawn.ContainsKey(pawn))
						{
							invCountForPawn.Add(pawn, count);
							inventoryForPawn.Add(pawn, new List<Thing> { thing });
						}
						else
						{
							invCountForPawn[pawn] += count;
							inventoriesAll[thingDef][pawn].Add(thing);
						}
					}
				}
			}

			// iterate over all required thingDefs
			foreach (var thingDef in reqTotalAll.Keys)
			{
				DebugLog($"--- Thing: {thingDef}");

#if DEBUG
				// debug output: required
				DebugLog($"- req total: {reqTotalAll[thingDef]}");
				foreach (var pair in reqCountPawns[thingDef])
					DebugLog($"{pair.Value} -> {pair.Key}");

				// debug output: inventory
				if (invTotalAll.ContainsKey(thingDef))
				{
					DebugLog($"- inv total: {invTotalAll[thingDef]}");
					foreach (var pair in invCountPawns[thingDef])
						DebugLog($"{pair.Value} -> {pair.Key}");
				}
#endif

				// check if any pawn has thingDef things in their inventory
				var invTotal = invTotalAll[thingDef];
				if (invTotal == 0)
				{
					DebugLog($"None of {thingDef} found in inventories; continuing");
					continue;
				}

				// get required and inventory count pawn lists for thingDef
				var reqCountPawn = reqCountPawns[thingDef];
				var invCountPawn = invCountPawns[thingDef];

				// if more is required than available in inventories, distribute the available amount in relation to the required amount per pawn
				var reqTotal = reqTotalAll[thingDef];
				if (reqTotal > invTotal)
				{
					var pawns = reqCountPawn.Keys.ToList();
					var ratio = invTotal / (float)reqTotal;
					var remainder = invTotal;
					foreach (var pawn in pawns)
					{
						var amount = (int)(reqCountPawn[pawn] * ratio);
						reqCountPawn[pawn] = amount;
						remainder -= amount;
					}
					var total = 0;
					foreach (var pawn in pawns)
					{
						var amount = reqCountPawn[pawn];
						if (remainder > 0)
						{
							amount += 1;
							remainder--;
						}
						if (amount == 0)
							reqCountPawn.Remove(pawn);
						else
							total += amount;
					}
					if (total != invTotal)
						Log.Warning($"{nameof(AutoAssignInventoryCaravan)}: Total ({total}) was not equal invTotal ({invTotal}) for {thingDef}; remainder is {remainder}, {reqTotal} was required, ratio is {ratio}");

#if DEBUG
					DebugLog($"- limiting (reqTotal > invTotal)");
					foreach (var pair in reqCountPawns[thingDef])
						DebugLog($"{pair.Value} -> {pair.Key}");
#endif
				}

				Distribute(thingDef, reqCountPawn, invCountPawn, inventoriesAll[thingDef]);
			}
		}

		private static void Distribute(
			ThingDef thingDef, 
			Dictionary<Pawn, int> reqThingDef,
			Dictionary<Pawn, int> invThingDef,
			Dictionary<Pawn, List<Thing>> inventories)
		{
			// check for required thingDefs already being in the correct inventory
			foreach (var pawn in reqThingDef.Keys.ToList())
			{
				// pawn requires thingDef and has it in inventory
				if (invThingDef.ContainsKey(pawn))
				{
					var inv = invThingDef[pawn];
					var req = reqThingDef[pawn];
					// has more in inventory than required
					if (inv > req)
					{
						// remove pawn from required-list and reduce inventory count for pawn
						reqThingDef.Remove(pawn);
						invThingDef[pawn] -= req;
					}
					// has exactly the amount required in inventory
					else if (inv == req)
					{
						// remove pawn from required- and inventory-list
						reqThingDef.Remove(pawn);
						invThingDef.Remove(pawn);
					}
					// has less in inventory than required
					else
					{
						// reduce required count and remove from inventory-list
						reqThingDef[pawn] -= inv;
						invThingDef.Remove(pawn);
					}
				}
			}

			DebugLog($"--- Looking for {thingDef}");

			// get pawns requiring thingDef and pawns with thingDef in their inventory as lists as to not cause problems when removing entries while iterating
			var reqPawns = reqThingDef.Keys.ToList();
			var invPawns = invThingDef.Keys.OrderBy((p) => p.IsColonist).ToList();

			// iterate over pawns requiring thingDef
			for (int i = 0; i < reqPawns.Count;)
			{
				var reqPawn = reqPawns[i];
				var req = reqThingDef[reqPawn];
				DebugLog($"req {req} {reqPawn}");

				// iterate over pawns with thingDef in their inventory
				for (int j = 0; j < invPawns.Count;)
				{
					var invPawn = invPawns[j];

					// at this point a pawn can only be in one of the two lists, not in both, as we already sorted the other cases out at the start of the method
					if (reqPawn == invPawn)
					{
						Log.Error($"Error trying to distribute item {thingDef.defName} between pawns; reqPawn ({reqPawn}) == invPawn ({invPawn})!");
						continue;
					}

					var inv = invThingDef[invPawn];
					DebugLog($"inv {inv} {invPawn}");

					int count;
					// invPawn has more thingDef in inventory than reqPawn requires
					if (inv > req)
					{
						// remove reqPawn from required-list and decrease amount of thingDef in invPawn inventory
						reqPawns.Remove(reqPawn);
						invThingDef[invPawn] -= req;
						count = req;
						j++;
					}
					// invPawn has exactly the amount of thingDef in inventory as reqPawn requires
					else if (inv == req)
					{
						// remove both pawns from their respective lists
						reqPawns.Remove(reqPawn);
						invPawns.Remove(invPawn);
						count = req;
					}
					// invPawn has fewer thingDef in inventory than reqPawn requires
					else
					{
						// remove invPawn from inventory-list and decrease amount of thingDef required for reqPawn 
						reqThingDef[reqPawn] -= inv;
						invPawns.Remove(invPawn);
						count = inv;
					}

					// decrease required and inventory counts
					DebugLog($"inv {invPawn} ({inv}) >> {count} >> req {reqPawn} ({req})");
					req -= count;
					inv -= count;

					// transfer the required things from invPawn's inventory to reqPawn
					TransferRequired(thingDef, count, reqPawn, invPawn, inventories[invPawn]);

					// stop searching through inventories when all thingDef-requirements for this pawn are met
					if (req == 0)
						break;
				}

				// make sure all required items have been distributed
				if (req != 0)
					Log.Error($"Error trying to distribute item {thingDef.defName} between pawns; {nameof(req)} != 0: {req} for {reqPawn}");
			}
		}


		private static void TransferRequired(ThingDef thingDef, int count, Pawn reqPawn, Pawn invPawn, List<Thing> thingsOnInvPawn)
		{
			var remainder = count;
			// iterate backwards to properly handle removed things
			for (int i = thingsOnInvPawn.Count - 1; i >= 0; i--)
			{
				var thing = thingsOnInvPawn[i];
				if (thing.def == thingDef)
				{
					// calculate transfer count
					var transferCount = Mathf.Min(remainder, thing.stackCount);

					// try to transfer things
					DebugLog($"Try to transfer {transferCount} / {thing.stackCount} of {thing} ({thingDef}) from {invPawn} to {reqPawn}");
					var resultCount = thing.holdingOwner.TryTransferToContainer(thing, reqPawn.inventory.innerContainer, transferCount, out Thing resultItem);
					if (resultItem?.stackCount != 0) // for some reason TryTransferToContainer returns 0 and an empty non-null result-item (stackCount = 0) when the whole stack was transferred,
						transferCount = resultCount; //  so in all other cases we take result-count as the amount transferred, otherwise we assume the desired amount was transferred
					DebugLog($"Transferred {transferCount} / {thing.stackCount} of {thing} ({thingDef}) from {invPawn} to {reqPawn} [result '{resultItem}' ({resultItem?.stackCount})]");

					// all of it has been transferred, remove thing
					if (transferCount == thing.stackCount)
						thingsOnInvPawn.Remove(thing);

					// subtract transfered count from remainder
					remainder -= transferCount;
					if (remainder == 0)
						break;
				}
			}
			if (remainder > 0)
				Log.Error($"{nameof(TransferRequired)} could not find enough items to transfer for {thingDef} on {invPawn}; {remainder} out of {count} left");
		}
		private static Dictionary<ThingDef, int> GetAllRequired(this Pawn pawn)
		{
			var result = new Dictionary<ThingDef, int>();
			// Drug policy "Keep in inventory"-assignments
			foreach (var entry in pawn.drugs.CurrentPolicy.entriesInt)
			{
				var thingDef = entry.drug;
				var count = entry.takeToInventory;
				if (count > 0)
				{
					if (!result.ContainsKey(thingDef))
						result.Add(thingDef, count);
					else
						result[thingDef] += count;
				}
			}
			// Assign-tab Carry-assignments
			foreach (var carry in pawn.inventoryStock.stockEntries)
			{
				var thingDef = carry.Value.thingDef;
				var count = carry.Value.count;
				if (count > 0)
				{
					if (!result.ContainsKey(thingDef))
						result.Add(thingDef, count);
					else
						result[thingDef] += count;
				}
			}
			return result;
		}
	}
}
