#region Copyright & License Information
/*
 * Copyright 2007-2017 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenRA.FileSystem;

namespace OpenRA.Mods.Common.UtilityCommands
{
	static class UpgradeRules
	{
		public const int MinimumSupportedVersion = 20161019;

		static void RenameNodeKey(MiniYamlNode node, string key)
		{
			var parts = node.Key.Split('@');
			node.Key = key;
			if (parts.Length > 1)
				node.Key += "@" + parts[1];
		}

		static void ConvertUpgradesToCondition(MiniYamlNode parent, MiniYamlNode node, string upgradesKey, string conditionKey)
		{
			var upgradesNode = node.Value.Nodes.FirstOrDefault(n => n.Key == upgradesKey);
			if (upgradesNode != null)
			{
				var conditions = FieldLoader.GetValue<string[]>("", upgradesNode.Value.Value);
				if (conditions.Length > 1)
					Console.WriteLine("Unable to automatically migrate {0}:{1} {2} to {3}. This must be corrected manually",
						parent.Key, node.Key, upgradesKey, conditionKey);
				else
					upgradesNode.Key = conditionKey;
			}
		}

		internal static void UpgradeActorRules(ModData modData, int engineVersion, ref List<MiniYamlNode> nodes, MiniYamlNode parent, int depth)
		{
			var addNodes = new List<MiniYamlNode>();

			foreach (var node in nodes)
			{
				// Add a warning to add WithRearmAnimation to actors that might need it.
				if (engineVersion < 20161020 && depth == 2)
				{
					if (node.Key == "RearmBuildings")
						foreach (var host in node.Value.Value.Split(','))
							Console.WriteLine("Actor type `{0}` is denoted as a RearmBuilding. Consider adding the `WithRearmAnimation` trait to it.".F(host));
				}

				// Resource type properties were renamed, and support for tooltips added
				if (engineVersion < 20161020)
				{
					if (node.Key.StartsWith("ResourceType", StringComparison.Ordinal))
					{
						var image = node.Value.Nodes.FirstOrDefault(n => n.Key == "Sequence");
						if (image != null)
							image.Key = "Image";

						var sequences = node.Value.Nodes.FirstOrDefault(n => n.Key == "Variants");
						if (sequences != null)
							sequences.Key = "Sequences";

						var name = node.Value.Nodes.FirstOrDefault(n => n.Key == "Name");
						if (name != null)
							node.Value.Nodes.Add(new MiniYamlNode("Type", name.Value.Value));
					}
				}

				// Renamed AttackSequence to DefaultAttackSequence in WithInfantryBody.
				if (engineVersion < 20161020)
				{
					if (node.Key == "WithInfantryBody")
					{
						var attackSequence = node.Value.Nodes.FirstOrDefault(n => n.Key == "AttackSequence");
						if (attackSequence != null)
							attackSequence.Key = "DefaultAttackSequence";
					}
				}

				// Move production description from Tooltip to Buildable
				if (engineVersion < 20161020)
				{
					var tooltipChild = node.Value.Nodes.FirstOrDefault(n => n.Key == "Tooltip" || n.Key == "DisguiseToolTip");
					if (tooltipChild != null)
					{
						var descNode = tooltipChild.Value.Nodes.FirstOrDefault(n => n.Key == "Description");
						if (descNode != null)
						{
							var buildableNode = node.Value.Nodes.FirstOrDefault(n => n.Key == "Buildable");
							if (buildableNode == null)
								node.Value.Nodes.Add(buildableNode = new MiniYamlNode("Buildable", ""));

							buildableNode.Value.Nodes.Add(descNode);
							tooltipChild.Value.Nodes.Remove(descNode);
						}
					}
				}

				// Move production icon sequence from Tooltip to Buildable
				if (engineVersion < 20161022)
				{
					var tooltipChild = node.Value.Nodes.FirstOrDefault(n => n.Key == "Tooltip" || n.Key == "DisguiseToolTip");
					if (tooltipChild != null)
					{
						var iconNode = tooltipChild.Value.Nodes.FirstOrDefault(n => n.Key == "Icon");
						if (iconNode != null)
						{
							var buildableNode = node.Value.Nodes.FirstOrDefault(n => n.Key == "Buildable");
							if (buildableNode == null)
								node.Value.Nodes.Add(buildableNode = new MiniYamlNode("Buildable", ""));

							buildableNode.Value.Nodes.Add(iconNode);
							tooltipChild.Value.Nodes.Remove(iconNode);
						}
					}
				}

				// Replaced upgrade consumers with conditions
				if (engineVersion < 20161117)
				{
					var upgradeTypesNode = node.Value.Nodes.FirstOrDefault(n => n.Key == "UpgradeTypes");
					if (upgradeTypesNode != null)
					{
						var upgradeMinEnabledLevel = 0;
						var upgradeMaxEnabledLevel = int.MaxValue;
						var upgradeMaxAcceptedLevel = 1;
						var upgradeTypes = FieldLoader.GetValue<string[]>("", upgradeTypesNode.Value.Value);
						var minEnabledNode = node.Value.Nodes.FirstOrDefault(n => n.Key == "UpgradeMinEnabledLevel");
						if (minEnabledNode != null)
							upgradeMinEnabledLevel = FieldLoader.GetValue<int>("", minEnabledNode.Value.Value);

						var maxEnabledNode = node.Value.Nodes.FirstOrDefault(n => n.Key == "UpgradeMaxEnabledLevel");
						if (maxEnabledNode != null)
							upgradeMaxEnabledLevel = FieldLoader.GetValue<int>("", maxEnabledNode.Value.Value);

						var maxAcceptedNode = node.Value.Nodes.FirstOrDefault(n => n.Key == "UpgradeMaxAcceptedLevel");
						if (maxAcceptedNode != null)
							upgradeMaxAcceptedLevel = FieldLoader.GetValue<int>("", maxAcceptedNode.Value.Value);

						var processed = false;
						if (upgradeMinEnabledLevel == 0 && upgradeMaxEnabledLevel == 0 && upgradeMaxAcceptedLevel == 1)
						{
							node.Value.Nodes.Add(new MiniYamlNode("RequiresCondition", upgradeTypes.Select(u => "!" + u).JoinWith(" && ")));
							processed = true;
						}
						else if (upgradeMinEnabledLevel == 1 && upgradeMaxEnabledLevel == int.MaxValue && upgradeMaxAcceptedLevel == 1)
						{
							node.Value.Nodes.Add(new MiniYamlNode("RequiresCondition", upgradeTypes.JoinWith(" || ")));
							processed = true;
						}
						else if (upgradeMinEnabledLevel == 0 && upgradeMaxEnabledLevel < int.MaxValue)
						{
							node.Value.Nodes.Add(new MiniYamlNode("RequiresCondition", upgradeTypes.JoinWith(" + ") + " <= " + upgradeMaxEnabledLevel));
							processed = true;
						}
						else if (upgradeMaxEnabledLevel == int.MaxValue && upgradeMinEnabledLevel > 1)
						{
							node.Value.Nodes.Add(new MiniYamlNode("RequiresCondition", upgradeTypes.JoinWith(" + ") + " >= " + upgradeMinEnabledLevel));
							processed = true;
						}
						else if (upgradeMaxEnabledLevel < int.MaxValue && upgradeMinEnabledLevel > 0)
						{
							var lowerBound = upgradeMinEnabledLevel + " <= " + upgradeTypes.JoinWith(" + ");
							var upperBound = upgradeTypes.JoinWith(" + ") + " <= " + upgradeMaxEnabledLevel;
							node.Value.Nodes.Add(new MiniYamlNode("RequiresCondition", lowerBound + " && " + upperBound));
							processed = true;
						}

						if (processed)
							node.Value.Nodes.RemoveAll(n => n.Key == "UpgradeTypes" || n.Key == "UpgradeMinEnabledLevel" ||
								n.Key == "UpgradeMaxEnabledLevel" || n.Key == "UpgradeMaxAcceptedLevel");
						else
							Console.WriteLine("Unable to automatically migrate {0}:{1} UpgradeTypes to RequiresCondition. This must be corrected manually", parent.Key, node.Key);
					}
				}

				if (engineVersion < 20161119)
				{
					// Migrated carryalls over to new conditions system
					ConvertUpgradesToCondition(parent, node, "CarryableUpgrades", "CarriedCondition");

					if (node.Key == "WithDecorationCarryable")
					{
						node.Key = "WithDecoration@CARRYALL";
						node.Value.Nodes.Add(new MiniYamlNode("RequiresCondition", "carryall-reserved"));
					}
				}

				if (engineVersion < 20161120)
				{
					if (node.Key.StartsWith("TimedUpgradeBar", StringComparison.Ordinal))
					{
						RenameNodeKey(node, "TimedConditionBar");
						ConvertUpgradesToCondition(parent, node, "Upgrade", "Condition");
					}

					if (node.Key.StartsWith("GrantUpgradePower", StringComparison.Ordinal))
					{
						Console.WriteLine("GrantUpgradePower Condition must be manually added to all target actor's ExternalConditions list.");
						RenameNodeKey(node, "GrantExternalConditionPower");
						ConvertUpgradesToCondition(parent, node, "Upgrades", "Condition");

						var soundNode = node.Value.Nodes.FirstOrDefault(n => n.Key == "GrantUpgradeSound");
						if (soundNode != null)
							soundNode.Key = "OnFireSound";
						else
							node.Value.Nodes.Add(new MiniYamlNode("OnFireSound", "ironcur9.aud"));

						var sequenceNode = node.Value.Nodes.FirstOrDefault(n => n.Key == "GrantUpgradeSequence");
						if (sequenceNode != null)
							sequenceNode.Key = "Sequence";
					}

					if (node.Key.StartsWith("GrantUpgradeCrateAction", StringComparison.Ordinal))
					{
						Console.WriteLine("GrantUpgradeCrateAction Condition must be manually added to all target actor's ExternalConditions list.");
						RenameNodeKey(node, "GrantExternalConditionCrateAction");
						ConvertUpgradesToCondition(parent, node, "Upgrades", "Condition");
					}
				}

				// Reworking bridge logic
				if (engineVersion < 20161210)
				{
					if (node.Key == "BridgeHut")
						RenameNodeKey(node, "LegacyBridgeHut");

					if (node.Key == "BridgeLayer")
						RenameNodeKey(node, "LegacyBridgeLayer");
				}

				// Removed WithBuildingExplosion
				if (engineVersion < 20161210)
				{
					if (node.Key == "WithBuildingExplosion")
					{
						node.Value.Nodes.Add(new MiniYamlNode("Type", "Footprint"));
						node.Value.Nodes.Add(new MiniYamlNode("Weapon", "UnitExplodeSmall"));
						node.Key = "Explodes";
						Console.WriteLine("The trait WithBuildingExplosion has been removed and superseded by additional 'Explodes' functionality.");
						Console.WriteLine("If you need a delayed building explosion, use 'Explodes' with 'Type: Footprint' and a cosmetic weapon with warhead delay.");
					}
				}

				if (engineVersion < 20161210)
				{
					// Migrated lua upgrades to conditions
					if (node.Key.StartsWith("ScriptUpgradesCache", StringComparison.Ordinal))
					{
						RenameNodeKey(node, "ExternalConditions");
						var conditions = node.Value.Nodes.FirstOrDefault(n => n.Key == "Upgrades");
						if (conditions != null)
							conditions.Key = "Conditions";
					}
				}

				if (engineVersion < 20161212)
				{
					if (node.Key.StartsWith("UpgradeActorsNear", StringComparison.Ordinal))
					{
						RenameNodeKey(node, "ProximityExternalCondition");
						ConvertUpgradesToCondition(parent, node, "Upgrades", "Condition");
					}

					if (node.Key == "Cargo")
						ConvertUpgradesToCondition(parent, node, "LoadingUpgrades", "LoadingCondition");

					if (node.Key == "Passenger" && node.Value.Nodes.Any(n => n.Key == "GrantUpgrades"))
					{
						Console.WriteLine("Passenger.GrantUpgrades support has been removed.");
						Console.WriteLine("Define passenger-conditions using Cargo.PassengerConditions on the transports instead.");
						node.Value.Nodes.RemoveAll(n => n.Key == "GrantUpgrades");
					}
				}

				if (engineVersion < 20161213)
				{
					if (node.Key == "Aircraft")
					{
						ConvertUpgradesToCondition(parent, node, "AirborneUpgrades", "AirborneCondition");
						ConvertUpgradesToCondition(parent, node, "CruisingUpgrades", "CruisingCondition");
					}

					if (node.Key.StartsWith("Cloak", StringComparison.Ordinal))
						ConvertUpgradesToCondition(parent, node, "WhileCloakedUpgrades", "CloakedCondition");

					if (node.Key == "Disguise")
					{
						ConvertUpgradesToCondition(parent, node, "Upgrades", "DisguisedCondition");
						if (!node.Value.Nodes.Any(n => n.Key == "DisguisedCondition"))
							node.Value.Nodes.Add(new MiniYamlNode("DisguisedCondition", "disguise"));
					}

					if (node.Key == "Parachutable")
					{
						ConvertUpgradesToCondition(parent, node, "ParachuteUpgrade", "ParachutingCondition");
						if (!node.Value.Nodes.Any(n => n.Key == "ParachutingCondition"))
							node.Value.Nodes.Add(new MiniYamlNode("ParachutingCondition", "parachute"));
					}

					if (node.Key == "PrimaryBuilding")
					{
						ConvertUpgradesToCondition(parent, node, "Upgrades", "PrimaryCondition");
						if (!node.Value.Nodes.Any(n => n.Key == "PrimaryCondition"))
							node.Value.Nodes.Add(new MiniYamlNode("PrimaryCondition", "primary"));
					}

					if (node.Key.StartsWith("UpgradeOnDamageState", StringComparison.Ordinal))
					{
						RenameNodeKey(node, "GrantConditionOnDamageState");
						ConvertUpgradesToCondition(parent, node, "Upgrades", "Condition");
					}

					if (node.Key.StartsWith("UpgradeOnMovement", StringComparison.Ordinal))
					{
						RenameNodeKey(node, "GrantConditionOnMovement");
						ConvertUpgradesToCondition(parent, node, "Upgrades", "Condition");
					}

					if (node.Key.StartsWith("UpgradeOnTerrain", StringComparison.Ordinal))
					{
						RenameNodeKey(node, "GrantConditionOnTerrain");
						ConvertUpgradesToCondition(parent, node, "Upgrades", "Condition");
						if (!node.Value.Nodes.Any(n => n.Key == "Condition"))
							node.Value.Nodes.Add(new MiniYamlNode("Condition", "terrain"));
					}

					if (node.Key == "AttackSwallow")
					{
						ConvertUpgradesToCondition(parent, node, "AttackingUpgrades", "AttackingCondition");
						if (!node.Value.Nodes.Any(n => n.Key == "AttackingCondition"))
							node.Value.Nodes.Add(new MiniYamlNode("AttackingCondition", "attacking"));
					}

					if (node.Key.StartsWith("Pluggable", StringComparison.Ordinal))
					{
						var upgrades = node.Value.Nodes.FirstOrDefault(n => n.Key == "Upgrades");
						if (upgrades != null)
						{
							upgrades.Key = "Conditions";
							foreach (var n in upgrades.Value.Nodes)
							{
								var conditions = FieldLoader.GetValue<string[]>("", n.Value.Value);
								if (conditions.Length > 1)
									Console.WriteLine("Unable to automatically migrate multiple Pluggable upgrades to a condition. This must be corrected manually");
							}
						}
					}

					if (node.Key.StartsWith("GlobalUpgradable", StringComparison.Ordinal))
					{
						RenameNodeKey(node, "GrantConditionOnPrerequisite");
						ConvertUpgradesToCondition(parent, node, "Upgrades", "Condition");
					}

					if (node.Key.StartsWith("GlobalUpgradeManager", StringComparison.Ordinal))
						RenameNodeKey(node, "GrantConditionOnPrerequisiteManager");

					if (node.Key.StartsWith("DeployToUpgrade", StringComparison.Ordinal))
					{
						RenameNodeKey(node, "GrantConditionOnDeploy");
						ConvertUpgradesToCondition(parent, node, "UndeployedUpgrades", "UndeployedCondition");
						ConvertUpgradesToCondition(parent, node, "DeployedUpgrades", "DeployedCondition");
					}

					if (node.Key == "GainsExperience")
					{
						var upgrades = node.Value.Nodes.FirstOrDefault(n => n.Key == "Upgrades");
						if (upgrades != null)
						{
							upgrades.Key = "Conditions";
							foreach (var n in upgrades.Value.Nodes)
							{
								var conditions = FieldLoader.GetValue<string[]>("", n.Value.Value);
								if (conditions.Length > 1)
									Console.WriteLine("Unable to automatically migrate multiple GainsExperience upgrades to a condition. This must be corrected manually");
							}
						}
					}

					if (node.Key.StartsWith("DisableOnUpgrade", StringComparison.Ordinal))
						RenameNodeKey(node, "DisableOnCondition");
				}

				if (engineVersion < 20161223)
				{
					if (node.Key.StartsWith("UpgradeManager", StringComparison.Ordinal))
						RenameNodeKey(node, "ConditionManager");

					if (node.Key.StartsWith("-UpgradeManager", StringComparison.Ordinal))
						RenameNodeKey(node, "-ConditionManager");
				}

				// Replaced NukePower CameraActor with CameraRange (effect-based reveal)
				if (engineVersion < 20161227)
				{
					var nukePower = node.Value.Nodes.FirstOrDefault(n => n.Key.StartsWith("NukePower", StringComparison.Ordinal));
					if (nukePower != null)
					{
						var cameraActor = nukePower.Value.Nodes.FirstOrDefault(n => n.Key == "CameraActor");
						if (cameraActor != null)
						{
							nukePower.Value.Nodes.Remove(cameraActor);
							nukePower.Value.Nodes.Add(new MiniYamlNode("CameraRange", "10"));
							Console.WriteLine("If your camera actor had a different reveal range than 10, you'll need to correct that manually");
						}
					}
				}

				// Capture bonus was decoupled from CashTrickler to a separate trait.
				if (engineVersion < 20170108 && depth == 0)
				{
					var trickler = node.Value.Nodes.FirstOrDefault(n => n.Key == "CashTrickler");
					if (trickler != null)
					{
						var capture = trickler.Value.Nodes.FirstOrDefault(n => n.Key == "CaptureAmount");
						if (capture != null)
						{
							var gcoc = new MiniYamlNode("GivesCashOnCapture", "");
							gcoc.Value.Nodes.Add(capture);
							trickler.Value.Nodes.Remove(capture);

							var show = trickler.Value.Nodes.FirstOrDefault(n => n.Key == "ShowTicks");
							if (show != null)
								gcoc.Value.Nodes.Add(show);

							node.Value.Nodes.Add(gcoc);
							RenameNodeKey(capture, "Amount");
						}

						var period = trickler.Value.Nodes.FirstOrDefault(n => n.Key == "Period");
						if (period != null)
							period.Key = "Interval";
					}
				}

				if (engineVersion < 20170121)
				{
					if (node.Key.StartsWith("ProvidesRadar", StringComparison.Ordinal))
					{
						if (node.Value.Nodes.Any(n => n.Key == "RequiresCondition"))
							Console.WriteLine("You must manually add the `disabled` condition to the ProvidesRadar RequiresCondition expression");
						else
							node.Value.Nodes.Add(new MiniYamlNode("RequiresCondition", "!disabled"));

						if (!parent.Value.Nodes.Any(n => n.Key == "GrantConditionOnDisabled@IDISABLE"))
							addNodes.Add(new MiniYamlNode("GrantConditionOnDisabled@IDISABLE", new MiniYaml("",
								new List<MiniYamlNode>() { new MiniYamlNode("Condition", "disabled") })));
					}

					if (node.Key.StartsWith("JamsRadar", StringComparison.Ordinal))
					{
						Console.WriteLine("JamsRadar has been replaced with trait conditions.");
						Console.WriteLine("You must manually add the `jammed` condition to the ProvidesRadar traits that you want to be affected.");
						Console.WriteLine("You must manually add a WithRangeCircle trait to render the radar jamming range.");
						RenameNodeKey(node, "ProximityExternalCondition@JAMMER");
						var stances = node.Value.Nodes.FirstOrDefault(n => n.Key == "Stances");
						if (stances != null)
							stances.Key = "ValidStances";
						else
							node.Value.Nodes.Add(new MiniYamlNode("ValidStances", "Enemy, Neutral"));

						node.Value.Nodes.Add(new MiniYamlNode("Condition", "jammed"));
					}
				}

				// Rename UpgradeOverlay to WithColoredOverlay
				if (engineVersion < 20170201)
					if (node.Key.StartsWith("UpgradeOverlay", StringComparison.Ordinal))
						RenameNodeKey(node, "WithColoredOverlay");

				// Remove SpiceBloom.RespawnDelay to get rid of DelayedAction, and rename GrowthDelay to Lifetime
				if (engineVersion < 20170203)
				{
					var spiceBloom = node.Value.Nodes.FirstOrDefault(n => n.Key == "SpiceBloom");
					if (spiceBloom != null)
					{
						var respawnDelay = spiceBloom.Value.Nodes.FirstOrDefault(n => n.Key == "RespawnDelay");
						if (respawnDelay != null)
						{
							spiceBloom.Value.Nodes.Remove(respawnDelay);
							Console.WriteLine("RespawnDelay has been removed from SpiceBloom for technical reasons.");
							Console.WriteLine("Increase self-kill delay of the spice bloom spawnpoint actor instead.");
						}

						var growthDelay = spiceBloom.Value.Nodes.FirstOrDefault(n => n.Key == "GrowthDelay");
						if (growthDelay != null)
							growthDelay.Key = "Lifetime";
					}
				}

				if (engineVersion < 20170205)
				{
					if (node.Key.StartsWith("SpiceBloom", StringComparison.Ordinal))
					{
						var spawnActor = node.Value.Nodes.FirstOrDefault(n => n.Key == "SpawnActor");
						if (spawnActor != null)
						{
							node.Value.Nodes.Remove(spawnActor);
							spawnActor.Key = "Actor";
						}
						else
							spawnActor = new MiniYamlNode("Actor", new MiniYaml("spicebloom.spawnpoint"));

						addNodes.Add(new MiniYamlNode("SpawnActorOnDeath", new MiniYaml("", new List<MiniYamlNode>() { spawnActor })));
					}
				}

				if (engineVersion < 20170210)
				{
					if (node.Key.StartsWith("AttackCharge", StringComparison.Ordinal))
						RenameNodeKey(node, "AttackTesla");

					if (node.Key.StartsWith("WithChargeOverlay", StringComparison.Ordinal))
						RenameNodeKey(node, "WithTeslaChargeOverlay");

					if (node.Key.StartsWith("WithChargeAnimation", StringComparison.Ordinal))
						RenameNodeKey(node, "WithTeslaChargeAnimation");
				}

				// Default values for ArrowSequence, CircleSequence and ClockSequence were changed (to null)
				if (engineVersion < 20170212)
				{
					var supportPowerNodes = new[] { "AirstrikePower", "ParatroopersPower", "NukePower" };

					if (supportPowerNodes.Any(s => node.Key.StartsWith(s, StringComparison.Ordinal)))
					{
						var arrow = node.Value.Nodes.FirstOrDefault(n => n.Key == "ArrowSequence");
						if (arrow == null)
							node.Value.Nodes.Add(new MiniYamlNode("ArrowSequence", "arrow"));

						var circle = node.Value.Nodes.FirstOrDefault(n => n.Key == "CircleSequence");
						if (circle == null)
							node.Value.Nodes.Add(new MiniYamlNode("CircleSequence", "circles"));

						var clock = node.Value.Nodes.FirstOrDefault(n => n.Key == "ClockSequence");
						if (clock == null)
							node.Value.Nodes.Add(new MiniYamlNode("ClockSequence", "clock"));
					}
				}

				if (engineVersion < 20170218)
				{
					var externalConditions = node.Value.Nodes.Where(n => n.Key.StartsWith("ExternalConditions", StringComparison.Ordinal));
					foreach (var ec in externalConditions.ToList())
					{
						var conditionsNode = ec.Value.Nodes.FirstOrDefault(n => n.Key == "Conditions");
						if (conditionsNode != null)
						{
							var conditions = FieldLoader.GetValue<string[]>("", conditionsNode.Value.Value);
							foreach (var c in conditions)
								node.Value.Nodes.Add(new MiniYamlNode("ExternalCondition@" + c.ToUpperInvariant(),
									new MiniYaml("", new List<MiniYamlNode>() { new MiniYamlNode("Condition", c) })));

							node.Value.Nodes.Remove(ec);
						}
					}
				}

				// Renamed DisguiseToolTip to DisguiseTooltip in Disguise.
				if (engineVersion < 20170303)
					if (node.Key.StartsWith("DisguiseToolTip", StringComparison.Ordinal))
						RenameNodeKey(node, "DisguiseTooltip");

				// Split UncloakOn: Damage => Damage, Heal, SelfHeal
				if (engineVersion < 20170528)
					if (node.Key.StartsWith("UncloakOn", StringComparison.Ordinal))
						node.Value.Value = node.Value.Value.Replace("Damage", "Damage, Heal, SelfHeal");

				// Removed dead ActorGroupProxy trait
				if (engineVersion < 20170528)
					node.Value.Nodes.RemoveAll(n => n.Key == "ActorGroupProxy");

				// Refactor SupplyTruck/AcceptsSupplies traits to DeliversCash/AcceptsDeliveredCash
				if (engineVersion < 20170528)
				{
					if (node.Key == "SupplyTruck")
						RenameNodeKey(node, "DeliversCash");
					if (node.Key == "-SupplyTruck")
						RenameNodeKey(node, "-DeliversCash");

					if (node.Key == "AcceptsSupplies")
						RenameNodeKey(node, "AcceptsDeliveredCash");
					if (node.Key == "-AcceptsSupplies")
						RenameNodeKey(node, "-AcceptsDeliveredCash");
				}

				// Add random sound support to AmbientSound
				if (engineVersion < 20170528)
					if (node.Key == "SoundFile" && parent.Key.StartsWith("AmbientSound", StringComparison.Ordinal))
						RenameNodeKey(node, "SoundFiles");

				// PauseOnLowPower property has been replaced with PauseOnCondition/RequiresCondition
				if (engineVersion < 20170528)
				{
					if (node.Key.StartsWith("WithRearmAnimation", StringComparison.Ordinal) || node.Key.StartsWith("WithRepairAnimation", StringComparison.Ordinal)
						|| node.Key.StartsWith("WithIdleAnimation", StringComparison.Ordinal))
					{
						var pauseOnLowPowerNode = node.Value.Nodes.FirstOrDefault(n => n.Key == "PauseOnLowPower");
						if (pauseOnLowPowerNode != null)
						{
							node.Value.Nodes.Remove(pauseOnLowPowerNode);
							Console.WriteLine("PauseOnLowPower has been removed from {0}; use RequiresCondition instead.".F(node.Key));
						}
					}
					else if (node.Key.StartsWith("WithIdleOverlay", StringComparison.Ordinal) || node.Key.StartsWith("WithRepairOverlay", StringComparison.Ordinal))
					{
						var pauseOnLowPowerNode = node.Value.Nodes.FirstOrDefault(n => n.Key == "PauseOnLowPower");
						if (pauseOnLowPowerNode != null)
						{
							node.Value.Nodes.Remove(pauseOnLowPowerNode);
							Console.WriteLine("PauseOnLowPower has been removed from {0}; use PauseOnCondition or RequiresCondition instead.".F(node.Key));
						}
					}
					else if (node.Key.StartsWith("AffectedByPowerOutage", StringComparison.Ordinal))
					{
						Console.WriteLine("Actor {0} has AffectedByPowerOutage; use the Condition property to apply its effects.".F(node.Key));
					}
					else if (node.Key.StartsWith("IonCannonPower", StringComparison.Ordinal) || node.Key.StartsWith("ProduceActorPower", StringComparison.Ordinal)
						|| node.Key.StartsWith("NukePower", StringComparison.Ordinal) || node.Key.StartsWith("AttackOrderPower", StringComparison.Ordinal)
						|| node.Key.StartsWith("GpsPower", StringComparison.Ordinal))
					{
						Console.WriteLine("{0} requires PauseOnCondition for pausing.".F(node.Key));
					}
				}

				if (engineVersion < 20170528)
				{
					if (node.Key == "Offset" && parent.Key.StartsWith("WithHarvestOverlay", StringComparison.Ordinal))
						RenameNodeKey(node, "LocalOffset");

					var gridType = modData.Manifest.Get<MapGrid>().Type;
					if (gridType == MapGridType.RectangularIsometric)
					{
						if (node.Key == "LocalOffset")
						{
							var orig = FieldLoader.GetValue<WVec[]>(node.Key, node.Value.Value);
							var scaled = orig.Select(o => FieldSaver.FormatValue(new WVec(
								(int)Math.Round(Math.Sqrt(2) * o.X),
								(int)Math.Round(Math.Sqrt(2) * o.Y),
								(int)Math.Round(Math.Sqrt(2) * o.Z))));
							node.Value.Value = scaled.JoinWith(", ");
						}

						if (node.Key == "Radius" && parent.Key == "Shape")
						{
							var orig = FieldLoader.GetValue<WDist>(node.Key, node.Value.Value);
							var scaled = (int)Math.Round(Math.Sqrt(2) * orig.Length);
							node.Value.Value = scaled.ToString();
						}

						if (node.Key == "TopLeft" || node.Key == "BottomRight" || node.Key == "PointA" || node.Key == "PointB")
						{
							var orig = FieldLoader.GetValue<int2>(node.Key, node.Value.Value);
							var scaled = new int2(
								(int)Math.Round(Math.Sqrt(2) * orig.X),
								(int)Math.Round(Math.Sqrt(2) * orig.Y));
							node.Value.Value = scaled.ToString();
						}

						if (node.Key == "VerticalTopOffset" || node.Key == "VerticalBottomOffset")
						{
							var orig = FieldLoader.GetValue<int>(node.Key, node.Value.Value);
							var scaled = (int)Math.Round(Math.Sqrt(2) * orig);
							node.Value.Value = scaled.ToString();
						}
					}
				}

				// Refactor Rectangle shape RotateToIsometry bool into WAngle LocalYaw
				if (engineVersion < 20170528)
				{
					if (node.Key.StartsWith("RotateToIsometry", StringComparison.Ordinal))
					{
						var value = FieldLoader.GetValue<bool>("RotateToIsometry", node.Value.Value);
						node.Value.Value = value ? "128" : "0";

						node.Key = "LocalYaw";
					}
				}

				// Removed GrantConditionOnDeploy.DeployAnimation and made WithMakeAnimation compatible instead
				if (engineVersion < 20170528)
				{
					var grantCondOnDeploy = node.Value.Nodes.FirstOrDefault(n => n.Key == "GrantConditionOnDeploy");
					if (grantCondOnDeploy != null)
					{
						var deployAnimNode = grantCondOnDeploy.Value.Nodes.FirstOrDefault(n => n.Key == "DeployAnimation");
						if (deployAnimNode != null)
						{
							grantCondOnDeploy.Value.Nodes.Remove(deployAnimNode);
							Console.WriteLine("DeployAnimation was removed from GrantConditionOnDeploy.");
							Console.WriteLine("Use WithMakeAnimation instead if a deploy animation is needed.");
						}
					}
				}

				// Added HitShape trait
				if (engineVersion < 20170531)
				{
					var hitShapeNode = new MiniYamlNode("HitShape", "");

					// Moved and renamed Health.Shape to HitShape.Type
					var health = node.Value.Nodes.FirstOrDefault(n => n.Key == "Health");
					if (health != null)
					{
						var shape = health.Value.Nodes.FirstOrDefault(n => n.Key == "Shape");
						if (shape != null)
						{
							RenameNodeKey(shape, "Type");
							hitShapeNode.Value.Nodes.Add(shape);
							node.Value.Nodes.Add(hitShapeNode);
							health.Value.Nodes.Remove(shape);
						}
						else
							node.Value.Nodes.Add(hitShapeNode);
					}

					// Moved ITargetablePositions from Building to HitShape
					var building = node.Value.Nodes.FirstOrDefault(n => n.Key == "Building");
					var hitShape = node.Value.Nodes.FirstOrDefault(n => n.Key == "HitShape");
					if (building != null && hitShape == null)
					{
						hitShapeNode.Value.Nodes.Add(new MiniYamlNode("UseOccupiedCellsOffsets", "true"));
						node.Value.Nodes.Add(hitShapeNode);
					}
				}

				// AutoTargetIgnore replaced with AutoTargetPriority and target types
				if (engineVersion < 20170610)
				{
					if (node.Key.StartsWith("AutoTarget", StringComparison.Ordinal) || node.Key.StartsWith("-AutoTarget", StringComparison.Ordinal))
					{
						Console.WriteLine("The AutoTarget traits have been reworked to use target types:");
						Console.WriteLine(" * Actors with AutoTarget must specify one or more AutoTargetPriority traits.");
						Console.WriteLine(" * The AutoTargetIgnore trait has been removed.");
						Console.WriteLine("   Append NoAutoTarget to the target types instead.");
					}
				}

				// Removed ApplyToAllTargetablePositions hack from Rectangle shape
				if (engineVersion < 20170629)
				{
					if (node.Key.StartsWith("HitShape", StringComparison.Ordinal))
					{
						var shape = node.Value.Nodes.FirstOrDefault(n => n.Key == "Type" && n.Value.Value == "Rectangle");
						if (shape != null)
						{
							var hack = shape.Value.Nodes.FirstOrDefault(n => n.Key == "ApplyToAllTargetablePositions");
							if (hack != null)
							{
								Console.WriteLine("Rectangle.ApplyToAllTargetablePositions has been removed due to incompatibilities");
								Console.WriteLine("with the HitShape refactor and projectile/warhead victim scans, as well as performance concerns.");
								Console.WriteLine("If you absolutely want to use it, please ship a duplicate of the old Rectangle code with your mod code.");
								Console.WriteLine("Otherwise, we recommend using inheritable shape templates for rectangular buildings");
								Console.WriteLine("and custom setups for the rest (see our official mods for examples).");
								shape.Value.Nodes.Remove(hack);
							}
						}
					}
				}

				// Refactor Building/Bib interaction, partially refactor and rename Bib
				if (engineVersion < 20170706)
				{
					var building = node.Value.Nodes.FirstOrDefault(n => n.Key == "Building");
					var bib = node.Value.Nodes.FirstOrDefault(n => n.Key == "Bib");

					var hasBib = false;
					if (bib != null)
					{
						var minibib = bib.Value.Nodes.FirstOrDefault(n => n.Key == "HasMinibib");
						if (minibib != null)
							hasBib = !FieldLoader.GetValue<bool>("HasMinibib", minibib.Value.Value);
						else
							hasBib = true;

						Console.WriteLine("Bibs are no longer automatically included in building footprints. Please check if any manual adjustments are needed.");
						RenameNodeKey(bib, "WithBuildingBib");
					}

					if (building != null && hasBib)
					{
						var footprint = building.Value.Nodes.FirstOrDefault(n => n.Key == "Footprint");
						var dimensions = building.Value.Nodes.FirstOrDefault(n => n.Key == "Dimensions");
						if (footprint != null && dimensions != null)
						{
							var newDim = FieldLoader.GetValue<CVec>("Dimensions", dimensions.Value.Value) + new CVec(0, 1);
							var oldFootprint = FieldLoader.GetValue<string>("Footprint", footprint.Value.Value);
							dimensions.Value.Value = newDim.ToString();
							footprint.Value.Value = oldFootprint + " " + string.Concat(Enumerable.Repeat("=", newDim.X));

							var gridType = modData.Manifest.Get<MapGrid>().Type;
							if (gridType == MapGridType.Rectangular)
								building.Value.Nodes.Add(new MiniYamlNode("LocalCenterOffset", "0,-512,0"));
						}
					}
				}

				// Bots must now specify an internal type as well as their display name
				if (engineVersion < 20170707)
				{
					if (node.Key.StartsWith("HackyAI", StringComparison.Ordinal) || node.Key.StartsWith("DummyAI", StringComparison.Ordinal))
					{
						var nameNode = node.Value.Nodes.FirstOrDefault(n => n.Key == "Name");

						// Just duplicate the name to avoid incompatibility with maps
						if (nameNode != null)
							node.Value.Nodes.Add(new MiniYamlNode("Type", nameNode.Value.Value));
					}
				}

				// We now differentiate between "occupied and provides offset" and "occupied but provides no offset",
				// so the old property name was no longer correct.
				if (engineVersion < 20170705)
				{
					if (node.Key == "UseOccupiedCellsOffsets")
						node.Key = "UseTargetableCellsOffsets";
				}

				// Refactored AttackBomb so it doesn't need it's own special sauce anymore
				if (engineVersion < 20170713)
				{
					if (node.Key == "AttackBomber")
					{
						var gunsOrBombs = node.Value.Nodes.FirstOrDefault(n => n.Key == "Guns" || n.Key == "Bombs");
						if (gunsOrBombs != null)
						{
							Console.WriteLine("Hardcoded Guns and Bombs logic has been removed from AttackBomber.");
							Console.WriteLine("Bombs should work like usual, for gun strafing use the new Weapon TargetOffset modifiers.");
							Console.WriteLine("Look at the TD mod's A10 for an example.");
							node.Value.Nodes.RemoveAll(n => n.Key == "Guns" || n.Key == "Bombs");
						}
					}
				}

				// Replace Mobile.OnRails hack with dedicated TDGunboat traits in Mods.Cnc
				if (engineVersion < 20170715)
				{
					var mobile = node.Value.Nodes.FirstOrDefault(n => n.Key == "Mobile");
					if (mobile != null)
					{
						var onRailsNode = mobile.Value.Nodes.FirstOrDefault(n => n.Key == "OnRails");
						var onRails = onRailsNode != null ? FieldLoader.GetValue<bool>("OnRails", onRailsNode.Value.Value) : false;
						if (onRails)
						{
							var speed = mobile.Value.Nodes.FirstOrDefault(n => n.Key == "Speed");
							var initFacing = mobile.Value.Nodes.FirstOrDefault(n => n.Key == "InitialFacing");
							var previewFacing = mobile.Value.Nodes.FirstOrDefault(n => n.Key == "PreviewFacing");
							var tdGunboat = new MiniYamlNode("TDGunboat", "");
							if (speed != null)
								tdGunboat.Value.Nodes.Add(speed);
							if (initFacing != null)
								tdGunboat.Value.Nodes.Add(initFacing);
							if (previewFacing != null)
								tdGunboat.Value.Nodes.Add(previewFacing);

							node.Value.Nodes.Add(tdGunboat);

							var attackTurreted = node.Value.Nodes.FirstOrDefault(n => n.Key.StartsWith("AttackTurreted", StringComparison.Ordinal));
							if (attackTurreted != null)
								RenameNodeKey(attackTurreted, "AttackTDGunboatTurreted");

							node.Value.Nodes.Remove(mobile);
						}
					}
				}

				// TargetWhenIdle and TargetWhenDamaged were removed from AutoTarget
				if (engineVersion < 20170722)
				{
					if (node.Key.StartsWith("AutoTarget", StringComparison.Ordinal))
					{
						var valueNodes = node.Value.Nodes;
						var targetIdle = valueNodes.FirstOrDefault(n => n.Key == "TargetWhenIdle");
						var targetDamaged = valueNodes.FirstOrDefault(n => n.Key == "TargetWhenDamaged");
						var hasInitialStance = valueNodes.FirstOrDefault(n => n.Key == "InitialStance") != null;
						var enableStances = valueNodes.FirstOrDefault(n => n.Key == "EnableStances");

						if (targetDamaged == null)
						{
							if (targetIdle != null)
							{
								if (hasInitialStance)
									Console.WriteLine("'TargetWhenIdle' was removed from 'AutoTarget'. 'InitialStance' might need to be adjusted.");
								else
								{
									valueNodes.Add(new MiniYamlNode("InitialStance", targetIdle.Value.Value.ToLower() == "true" ? "Defend" : "ReturnFire"));

									if (enableStances != null)
										enableStances.Value.Value = "false";
									else
										valueNodes.Add(new MiniYamlNode("EnableStances", "false"));
								}

								valueNodes.Remove(targetIdle);
							}
						}
						else
						{
							if (targetIdle == null)
							{
								if (hasInitialStance)
									Console.WriteLine("'TargetWhenDamaged' was removed from 'AutoTarget'. 'InitialStance' might need to be adjusted.");
								else
								{
									// In this case the default for "TargetWhenIdle" (true) takes effect, i.e. use the "Defend" stance
									valueNodes.Add(new MiniYamlNode("InitialStance", "Defend"));

									if (enableStances != null)
										enableStances.Value.Value = "false";
									else
										valueNodes.Add(new MiniYamlNode("EnableStances", "false"));
								}

								valueNodes.Remove(targetDamaged);
							}
							else
							{
								if (hasInitialStance)
									Console.WriteLine("'TargetWhenDamaged' and 'TargetWhenIdle' were removed from 'AutoTarget'. 'InitialStance' might need to be adjusted.");
								else
								{
									var idle = targetIdle.Value.Value.ToLower() == "true";
									var damaged = targetDamaged.Value.Value.ToLower() == "true";

									if (idle)
										valueNodes.Add(new MiniYamlNode("InitialStance", "Defend"));
									else
										valueNodes.Add(new MiniYamlNode("InitialStance", damaged ? "ReturnFire" : "HoldFire"));

									if (enableStances != null)
										enableStances.Value.Value = "false";
									else
										valueNodes.Add(new MiniYamlNode("EnableStances", "false"));
								}

								valueNodes.Remove(targetIdle);
								valueNodes.Remove(targetDamaged);
							}
						}
					}
				}

				// Introduced TakeOffOnCreation and TakeOffOnResupply booleans to aircraft
				if (engineVersion < 20170819)
				{
					if (node.Key.StartsWith("Aircraft", StringComparison.Ordinal))
					{
						var canHover = node.Value.Nodes.FirstOrDefault(n => n.Key == "CanHover");
						var isHeli = canHover != null ? FieldLoader.GetValue<bool>("CanHover", canHover.Value.Value) : false;
						if (isHeli)
						{
							Console.WriteLine("Helicopters taking off automatically while planes don't is no longer hardcoded.");
							Console.WriteLine("Instead, this is controlled via the TakeOffOnResupply field.");
							Console.WriteLine("Please check if your aircraft behave as intended or need manual adjustments.");
							node.Value.Nodes.Add(new MiniYamlNode("TakeOffOnResupply", "true"));
						}

						// Upgrade rule for setting VTOL to true for CanHover actors
						if (isHeli)
							node.Value.Nodes.Add(new MiniYamlNode("VTOL", "true"));
					}
				}

				// nuke launch animation is now it's own trait
				if (engineVersion < 20170820)
				{
					if (depth == 1 && node.Key.StartsWith("NukePower"))
					{
						node.Value.Nodes.RemoveAll(n => n.Key == "ActivationSequence");
						addNodes.Add(new MiniYamlNode("WithNukeLaunchAnimation", new MiniYaml("")));
					}
				}

				UpgradeActorRules(modData, engineVersion, ref node.Value.Nodes, node, depth + 1);
			}

			foreach (var a in addNodes)
				nodes.Add(a);
		}

		internal static void UpgradeWeaponRules(ModData modData, int engineVersion, ref List<MiniYamlNode> nodes, MiniYamlNode parent, int depth)
		{
			foreach (var node in nodes)
			{
				// Rename LaserZap BeamDuration to just Duration
				if (engineVersion < 20161020)
				{
					if (node.Key == "BeamDuration")
						node.Key = "Duration";
				}

				// Rename Bullet Angle to LaunchAngle
				if (engineVersion < 20161020)
				{
					if (node.Key == "Angle")
						node.Key = "LaunchAngle";
				}

				if (engineVersion < 20161120)
				{
					if (node.Key.StartsWith("Warhead", StringComparison.Ordinal) && node.Value.Value == "GrantUpgrade")
					{
						node.Value.Value = "GrantExternalCondition";
						Console.WriteLine("GrantExternalCondition Condition must be manually added to all target actor's ExternalConditions list.");
						ConvertUpgradesToCondition(parent, node, "Upgrades", "Condition");
					}
				}

				// Rename LaserZap TracksTarget to TrackTarget
				if (engineVersion < 20161217)
				{
					if (node.Key == "TracksTarget")
						node.Key = "TrackTarget";
				}

				// Refactor GravityBomb Speed WDist to Velocity WVec and Acceleration from vertical WDist to vector
				if (engineVersion < 20170528)
				{
					var projectile = node.Value.Nodes.FirstOrDefault(n => n.Key == "Projectile");
					if (projectile != null && projectile.Value.Value == "GravityBomb")
					{
						var speedNode = projectile.Value.Nodes.FirstOrDefault(x => x.Key == "Speed");
						if (speedNode != null)
						{
							var oldWDistSpeed = FieldLoader.GetValue<string>("Speed", speedNode.Value.Value);
							speedNode.Value.Value = "0, 0, -" + oldWDistSpeed;
							speedNode.Key = "Velocity";
						}

						var accelNode = projectile.Value.Nodes.FirstOrDefault(x => x.Key == "Acceleration");
						if (accelNode != null)
						{
							var oldWDistAccel = FieldLoader.GetValue<string>("Acceleration", accelNode.Value.Value);
							accelNode.Value.Value = "0, 0, -" + oldWDistAccel;
						}
					}
				}

				// Optimal victim scan radii are now calculated automatically
				if (engineVersion < 20170528)
				{
					var targetExtraSearchRadius = node.Value.Nodes.FirstOrDefault(n => n.Key == "TargetSearchRadius" || n.Key == "TargetExtraSearchRadius");
					if (targetExtraSearchRadius != null)
					{
						Console.WriteLine("Warheads and projectiles now calculate the best victim search radius automatically.");
						Console.WriteLine("If you absolutely need to override that for whatever reason, use the new fields:");
						Console.WriteLine("VictimScanRadius for warheads, BlockerScanRadius for projectiles,");
						Console.WriteLine("BounceBlockerScanRadius for bouncing Bullets and AreaVictimScanRadius for AreaBeams.");
						node.Value.Nodes.Remove(targetExtraSearchRadius);
					}
				}

				// Valid-/InvalidImpactTypes were removed from CreateEffectWarhead, which uses Valid-/InvalidTargets instead now
				if (engineVersion < 20170625)
				{
					if (node.Key.StartsWith("Warhead", StringComparison.Ordinal) && node.Value.Value == "CreateEffect")
					{
						var validImpactTypes = node.Value.Nodes.FirstOrDefault(n => n.Key == "ValidImpactTypes");
						var invalidImpactTypes = node.Value.Nodes.FirstOrDefault(n => n.Key == "InvalidImpactTypes");
						var validTargetsNode = node.Value.Nodes.FirstOrDefault(n => n.Key == "ValidTargets");
						if (validImpactTypes != null && validTargetsNode == null)
						{
							Console.WriteLine("CreateEffectWarhead now uses Valid-/InvalidTargets instead of Valid-/InvalidImpactTypes.");
							Console.WriteLine("Please check whether you need to make manual adjustments.");

							var validTargets = new List<string>();
							if (validImpactTypes.Value.Value.Contains("Ground"))
								validTargets.Add("Ground");
							if (validImpactTypes.Value.Value.Contains("Water"))
								validTargets.Add("Water");
							if (validImpactTypes.Value.Value.Contains("Air"))
								validTargets.Add("Air");

							// 'validTargets' can be 0 here if the only valid ImpactType(s) were None, TargetHit or TargetTerrain.
							// In that case we remove it and let the modder fix it manually.
							if (validTargets.Count > 0)
							{
								validImpactTypes.Value.Value = validTargets.JoinWith(", ");
								RenameNodeKey(validImpactTypes, "ValidTargets");
							}
							else
								node.Value.Nodes.Remove(validImpactTypes);
						}
						else if (validTargetsNode == null)
						{
							// 'Air' is not part of the internal warhead ValidTargets default, but was part of the ValidImpactTypes default.
							node.Value.Nodes.Add(new MiniYamlNode("ValidTargets", "Ground, Water, Air"));
						}

						if (invalidImpactTypes != null)
						{
							Console.WriteLine("CreateEffectWarhead now uses Valid-/InvalidTargets instead of Valid-/InvalidImpactTypes.");
							Console.WriteLine("Please check whether you need to make manual adjustments.");

							// It's too complicated to get all possible combinations right, so we just remove it and let the modder fix it manually
							node.Value.Nodes.Remove(invalidImpactTypes);
						}
					}
				}

				// Made Missile terrain height checks disableable and disabled by default
				if (engineVersion < 20170713)
				{
					var gridMaxHeight = modData.Manifest.Get<MapGrid>().MaximumTerrainHeight;
					if (gridMaxHeight > 0)
					{
						var projectile = node.Value.Nodes.FirstOrDefault(n => n.Key == "Projectile");
						if (projectile != null && projectile.Value.Value == "Missile")
							projectile.Value.Nodes.Add(new MiniYamlNode("TerrainHeightAware", "true"));
					}
				}

				// Rename BurstDelay to BurstDelays
				if (engineVersion < 20170818)
					if (node.Key == "BurstDelay")
						node.Key = "BurstDelays";

				UpgradeWeaponRules(modData, engineVersion, ref node.Value.Nodes, node, depth + 1);
			}
		}

		internal static void UpgradeSequences(ModData modData, int engineVersion, ref List<MiniYamlNode> nodes, MiniYamlNode parent, int depth)
		{
			foreach (var node in nodes)
			{
				// Add rules here
				UpgradeSequences(modData, engineVersion, ref node.Value.Nodes, node, depth + 1);
			}
		}

		internal static void UpgradeTileset(ModData modData, int engineVersion, ref List<MiniYamlNode> nodes, MiniYamlNode parent, int depth)
		{
			foreach (var node in nodes)
			{
				// Renamed Category to Categories in Template.
				if (engineVersion < 20170623)
				{
					if (node.Key == "Template" || node.Key.StartsWith("Template@", StringComparison.Ordinal))
					{
						var category = node.Value.Nodes.FirstOrDefault(n => n.Key == "Category");
						if (category != null)
							category.Key = "Categories";
					}
				}

				UpgradeTileset(modData, engineVersion, ref node.Value.Nodes, node, depth + 1);
			}
		}

		internal static void UpgradeCursors(ModData modData, int engineVersion, ref List<MiniYamlNode> nodes, MiniYamlNode parent, int depth)
		{
			foreach (var node in nodes)
			{
				// Add rules here
				UpgradeCursors(modData, engineVersion, ref node.Value.Nodes, node, depth + 1);
			}
		}

		internal static void UpgradePlayers(ModData modData, int engineVersion, ref List<MiniYamlNode> nodes, MiniYamlNode parent, int depth)
		{
			foreach (var node in nodes)
			{
				// Add rules here
				UpgradePlayers(modData, engineVersion, ref node.Value.Nodes, node, depth + 1);
			}
		}

		internal static void UpgradeChromeMetrics(ModData modData, int engineVersion, ref List<MiniYamlNode> nodes, MiniYamlNode parent, int depth)
		{
			foreach (var node in nodes)
			{
				// Add rules here
				UpgradeChromeMetrics(modData, engineVersion, ref node.Value.Nodes, node, depth + 1);
			}
		}

		internal static void UpgradeChromeLayout(ModData modData, int engineVersion, ref List<MiniYamlNode> nodes, MiniYamlNode parent, int depth)
		{
			foreach (var node in nodes)
			{
				// Add rules here
				UpgradeChromeLayout(modData, engineVersion, ref node.Value.Nodes, node, depth + 1);
			}
		}

		internal static void ModifyCPos(ref string input, CVec vector)
		{
			var oldCPos = FieldLoader.GetValue<CPos>("(value)", input);
			var newCPos = oldCPos + vector;
			input = newCPos.ToString();
		}

		internal static void UpgradeActors(ModData modData, int engineVersion, ref List<MiniYamlNode> nodes, MiniYamlNode parent, int depth)
		{
			foreach (var node in nodes)
			{
				// Add rules here
				UpgradeActors(modData, engineVersion, ref node.Value.Nodes, node, depth + 1);
			}
		}

		internal static void UpgradeMapFormat(ModData modData, IReadWritePackage package)
		{
			if (package == null)
				return;

			var yamlStream = package.GetStream("map.yaml");
			if (yamlStream == null)
				return;

			var yaml = new MiniYaml(null, MiniYaml.FromStream(yamlStream, package.Name));
			var nd = yaml.ToDictionary();
			var mapFormat = FieldLoader.GetValue<int>("MapFormat", nd["MapFormat"].Value);
			if (mapFormat < 11)
				throw new InvalidDataException("Map format {0} is not supported.\n File: {1}".F(mapFormat, package.Name));

			if (mapFormat < Map.SupportedMapFormat)
			{
				yaml.Nodes.First(n => n.Key == "MapFormat").Value = new MiniYaml(Map.SupportedMapFormat.ToString());
				Console.WriteLine("Converted {0} to MapFormat {1}.", package.Name, Map.SupportedMapFormat);
			}

			package.Update("map.yaml", Encoding.UTF8.GetBytes(yaml.Nodes.WriteToString()));
		}
	}
}
