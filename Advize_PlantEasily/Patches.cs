﻿using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Advize_PlantEasily
{
    public partial class PlantEasily
    {
        [HarmonyPatch(typeof(Player), "UpdateBuildGuiInput")]
        public class PlayerUpdateBuildGuiInput
        {
            public static void Prefix(Player __instance)
            {
                if (Input.GetKeyUp(config.EnableModKey))
                {
                    config.ModActive = !config.ModActive;
                    Dbgl($"modActive was {!config.ModActive} setting to {config.ModActive}");
                    if (__instance.GetRightItem()?.m_shared.m_name == "$item_cultivator")
                    {
                        typeof(Player).GetMethod("SetupPlacementGhost", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(__instance, new object[0]);
                    }
                }
                if (Input.GetKeyUp(config.EnableSnappingKey))
                {
                    config.SnapActive = !config.SnapActive;
                    Dbgl($"snapActive was {!config.SnapActive} setting to {config.SnapActive}");
                }
                
                if (Input.GetKey(config.KeyboardModifierKey) || Input.GetKey(config.GamepadModifierKey))
                {
                    if (Input.GetKeyUp(config.IncreaseXKey) || ZInput.GetButtonDown("JoyDPadRight"))
                        config.Columns += 1;
                    
                    if (Input.GetKeyUp(config.IncreaseYKey) || ZInput.GetButtonDown("JoyDPadUp"))
                        config.Rows += 1;
                    
                    if (Input.GetKeyUp(config.DecreaseXKey) || ZInput.GetButtonDown("JoyDPadLeft"))
                        config.Columns -= 1;
                    
                    if (Input.GetKeyUp(config.DecreaseYKey) || ZInput.GetButtonDown("JoyDPadDown"))
                        config.Rows -= 1;
                }
            }
        }
        
        [HarmonyPatch(typeof(HotkeyBar), "Update")]
        public class HotKeyBarUpdate
        {
            public static void Prefix(ref bool __runOriginal)
            {
                if (OverrideGamepadInput() && Player.m_localPlayer && !InventoryGui.IsVisible() && !Menu.IsVisible() && !GameCamera.InFreeFly() && !Minimap.IsOpen())
                {
                    __runOriginal = false;
                }
            }
        }
        
        [HarmonyPatch(typeof(Player), "StartGuardianPower")]
        public class PlayerStartGuardianPower
        {
            public static void Prefix(ref bool __runOriginal)
            {
                __runOriginal = !OverrideGamepadInput();
            }
        }
        
        [HarmonyPatch(typeof(Player), "SetupPlacementGhost")]
        public class PlayerSetupPlacementGhost
        {
            public static void Postfix(ref GameObject ___m_placementGhost)
            {
                //Dbgl("SetupPlacementGhost");
                DestroyGhosts();
                
                if (!config.ModActive || !___m_placementGhost)
                    return;
                
                if (___m_placementGhost.GetComponent<Piece>().m_repairPiece)
                    return;
                
                if (!___m_placementGhost.GetComponent<Plant>() && !___m_placementGhost.GetComponent<Pickable>())
                    return;
                
                CreateGhosts(___m_placementGhost);
            }
        }
        
        [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
        public class PlayerUpdatePlacementGhost
        {
            public static void Postfix(Player __instance, bool ___m_noPlacementCost, ref GameObject ___m_placementGhost, ref int ___m_placementStatus)
            {
                if (!config.ModActive || !___m_placementGhost || (!___m_placementGhost.GetComponent<Plant>() && !___m_placementGhost.GetComponent<Pickable>()))
                    return;
                
                if (ghostPlacementStatus.Count == 0 || (extraGhosts.Count == 0 && !(config.Rows == 1 && config.Columns == 1))) //If there are no extra ghosts but there is supposed to be
                    typeof(Player).GetMethod("SetupPlacementGhost", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(__instance, new object[0]);
                
                for (int i = 0; i < extraGhosts.Count; i++)
                {
                    GameObject extraGhost = extraGhosts[i];
                    extraGhost.SetActive(___m_placementGhost.activeSelf);
                    SetPlacementGhostStatus(extraGhost, i + 1, Status.Healthy, ref ___m_placementStatus);
                }
                SetPlacementGhostStatus(___m_placementGhost, 0, Status.Healthy, ref ___m_placementStatus);

                Vector3 basePosition = ___m_placementGhost.transform.position;
                Quaternion baseRotation = ___m_placementGhost.transform.rotation;

                float pieceSpacing = GetPieceSpacing(___m_placementGhost);

                // Takes position of ghost, subtracts position of player to get vector between the two and facing out from the player, normalizes that vector to have a magnitude of 1.0f
                Vector3 rowDirection = Utils.DirectionXZ(basePosition - __instance.transform.position);
                // Cross product of a vertical vector and the forward facing normalized vector, producing a perpendicular lateral vector
                Vector3 columnDirection = Vector3.Cross(Vector3.up, rowDirection);
                
                bool foundSnaps = false;
                if (config.SnapActive)
                {
                    List<Vector3> snapPoints = new();
                    Plant plant = ___m_placementGhost.GetComponent<Plant>();

                    Collider[] obstructions = Physics.OverlapSphere(___m_placementGhost.transform.position, pieceSpacing, snapCollisionMask);
                    int validFirstOrderCollisions = 0;
                    
                    foreach (Collider collider in obstructions)
                    {
                        if (!collider.GetComponent<Plant>() && !collider.GetComponentInParent<Pickable>()) continue;
                        validFirstOrderCollisions++;
                        if (validFirstOrderCollisions > 8) break;
                        
                        Collider[] secondaryObstructions = Physics.OverlapSphere(collider.transform.position, pieceSpacing, snapCollisionMask);
                        int validSecondOrderCollisions = 0;
                        
                        foreach (Collider secondaryCollider in secondaryObstructions)
                        {
                            if (!secondaryCollider.GetComponent<Plant>() && !secondaryCollider.GetComponentInParent<Pickable>()) continue;
                            if (secondaryCollider.transform.root == collider.transform.root) continue;
                            validSecondOrderCollisions++;
                            if (validSecondOrderCollisions > 8) break;
                                    
                            rowDirection = Utils.DirectionXZ(secondaryCollider.transform.position - collider.transform.position);
                            columnDirection = Vector3.Cross(Vector3.up, rowDirection);
                                    
                            rowDirection = baseRotation * rowDirection * pieceSpacing;
                            columnDirection = baseRotation * columnDirection * pieceSpacing;

                            List<Vector3> positions = new()
                            {
                                collider.transform.position + rowDirection,
                                collider.transform.position + columnDirection,
                                collider.transform.position - rowDirection,
                                collider.transform.position - columnDirection
                            };

                            foreach (Vector3 pos in positions)
                            {
                                if (!Physics.CheckSphere(pos, 0f, snapCollisionMask))
                                {
                                    if (plant && !HasGrowSpace(plant, pos)) continue;

                                    snapPoints.Add(pos);
                                    foundSnaps = true;
                                }
                            }
                        }

                        if (!foundSnaps)
                        {
                            rowDirection = baseRotation * rowDirection * pieceSpacing;
                            columnDirection = baseRotation * columnDirection * pieceSpacing;

                            List<Vector3> positions = new()
                            {
                                collider.transform.position + rowDirection,
                                collider.transform.position + columnDirection,
                                collider.transform.position - rowDirection,
                                collider.transform.position - columnDirection
                            };

                            foreach (Vector3 pos in positions)
                            {
                                if (!Physics.CheckSphere(pos, 0f, snapCollisionMask))
                                {
                                    if (plant && !HasGrowSpace(plant, pos)) continue;

                                    snapPoints.Add(pos);
                                    foundSnaps = true;
                                }
                            }
                        }
                    }
                        
                    if (foundSnaps)
                    {
                        Vector3 firstSnapPos = snapPoints.OrderBy(o => snapPoints.Min(m => Utils.DistanceXZ(m, o)) + (o - basePosition).magnitude).First();
                        basePosition = ___m_placementGhost.transform.position = firstSnapPos;
                    }
                }
                
                if (!foundSnaps)
                {
                    rowDirection = Utils.DirectionXZ(basePosition - __instance.transform.position);
                    columnDirection = Vector3.Cross(Vector3.up, rowDirection);
                    // Can't do this all on one line it bugs and I can't understand why
                    rowDirection *= pieceSpacing;
                    columnDirection *= pieceSpacing;
                }
                
                Piece.Requirement resource = ___m_placementGhost.GetComponent<Piece>().m_resources[0];
                int cost = resource.m_amount;
                int currentCost = 0;
                
                for (int row = 0; row < config.Rows; row++)
                {
                    for (int column = 0; column < config.Columns; column++)
                    {
                        currentCost += cost;
                        bool isRoot = (column == 0 && row == 0);
                        int ghostIndex = row * config.Columns + column;
                        GameObject ghost = isRoot ? ___m_placementGhost : extraGhosts[ghostIndex - 1];
                        
                        Vector3 ghostPosition = isRoot ? basePosition : basePosition + rowDirection * row + columnDirection * column;
                        
                        Heightmap.GetHeight(ghostPosition, out float height);
                        ghostPosition.y = height;
                        
                        ghost.transform.position = ghostPosition;
                        ghost.transform.rotation = ___m_placementGhost.transform.rotation;

                        if (!___m_noPlacementCost && __instance.GetInventory().CountItems(resource.m_resItem.m_itemData.m_shared.m_name) < currentCost)
                        {
                            SetPlacementGhostStatus(ghost, ghostIndex, Status.LackResources, ref ___m_placementStatus);
                        }

                        SetPlacementGhostStatus(ghost, ghostIndex, CheckPlacementStatus(ghost, ghostPlacementStatus[ghostIndex]), ref ___m_placementStatus);
                    }
                }
            }
        }
        
        [HarmonyPatch(typeof(Player), "PlacePiece")]
        public class PlayerPlacePiece
        {
            public static bool Prefix(Piece piece, bool ___m_noPlacementCost, GameObject ___m_placementGhost, ref int ___m_placementStatus, ref bool __result)
            {
                //Dbgl("Player.PlacePiece Prefix");
                if (!config.ModActive || !piece || (!piece.GetComponent<Plant>() && !piece.GetComponent<Pickable>()))
                    return true;

                if (config.PreventInvalidPlanting)
                {
                    int rootPlacementStatus = (int)CheckPlacementStatus(___m_placementGhost);
                    if (rootPlacementStatus > 1)
                    {
                        Player.m_localPlayer.Message(MessageHud.MessageType.Center, statusMessage[rootPlacementStatus]);
                        SetPlacementGhostStatus(___m_placementGhost, 0, Status.Invalid, ref ___m_placementStatus);
                        __result = false;
                        return false;
                    }
                }
                
                if (config.PreventPartialPlanting)
                {
                    foreach (int i in ghostPlacementStatus)
                    {
                        if (i != 0)
                        {
                            if (i == 1 && ___m_noPlacementCost)
                                continue;

                            Player.m_localPlayer.Message(MessageHud.MessageType.Center, statusMessage[i]);
                            SetPlacementGhostStatus(___m_placementGhost, 0, Status.Invalid, ref ___m_placementStatus);
                            __result = false;
                            return false;
                        }
                    }
                }
                return true;
            }
            
            public static void Postfix(Player __instance, Piece piece, bool ___m_noPlacementCost)
            {
                //Dbgl("Player.PlacePiece Postfix");
                if (!config.ModActive || !piece || (!piece.GetComponent<Plant>() && !piece.GetComponent<Pickable>()))
                    return;
                
                //This doesn't apply to the root placement ghost.
                if (ghostPlacementStatus[0] == Status.Healthy) // With this, root Ghost must be valid (can be fixed)
                {
                    ItemDrop.ItemData rightItem = __instance.GetRightItem();
                    int count = extraGhosts.Count;

                    for (int i = 0; i < extraGhosts.Count; i++)
                    {
                        if (ghostPlacementStatus[i + 1] != Status.Healthy)
                        {
                            if (ghostPlacementStatus[i + 1] == Status.LackResources && ___m_noPlacementCost)
                                count--;
                            else
                                continue;
                        }

                        Vector3 position = extraGhosts[i].transform.position;
                        Quaternion rotation = config.RandomizeRotation ? Quaternion.Euler(0f, 22.5f * Random.Range(0, 16), 0f) : extraGhosts[i].transform.rotation;
                        GameObject gameObject = piece.gameObject;

                        TerrainModifier.SetTriggerOnPlaced(trigger: true);
                        GameObject gameObject2 = Instantiate(gameObject, position, rotation);
                        TerrainModifier.SetTriggerOnPlaced(trigger: false);

                        gameObject2.GetComponent<Piece>()?.SetCreator(__instance.GetPlayerID());
                        gameObject2.GetComponent<PrivateArea>()?.Setup(Game.instance.GetPlayerProfile().GetName());

                        piece.m_placeEffect.Create(position, rotation, gameObject2.transform, 1f);
                        __instance.AddNoise(50f);

                        Game.instance.GetPlayerProfile().m_playerStats.m_builds++;
                        ZLog.Log("Placed " + gameObject.name);
                        Gogan.LogEvent("Game", "PlacedPiece", gameObject.name, 0L);
                    }
                    for (int i = 0; i < count; i++)
                    {
                        __instance.ConsumeResources(piece.m_resources, 0);
                    }
                    if (rightItem.m_shared.m_useDurability && config.UseDurability)
                    {
                        rightItem.m_durability -= rightItem.m_shared.m_useDurabilityDrain * count;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Player), "Interact")]
        public class PlayerInteract
        {
            public static void Prefix(Player __instance, GameObject go, bool hold, bool alt, float ___m_lastHoverInteractTime)
            {
                if (!config.ModActive || !config.EnableMassHarvest || __instance.InAttack() || __instance.InDodge() || (hold && Time.time - ___m_lastHoverInteractTime < 0.2f))
                    return;

                if (!Input.GetKey(config.KeyboardHarvestModifierKey) && !Input.GetKey(config.GamepadModifierKey))
                    return;
                
                Interactable componentInParent = go.GetComponentInParent<Interactable>();
                Pickable pickable = (Pickable)componentInParent;

                if (pickable)
                {
                    //List<Pickable> extraPickables = config.HarvestResourcesInRadius ? FindResourcesInRadius(pickable) : FindConnectedResources(pickable);
                    
                    foreach (Pickable extraPickable in FindResourcesInRadius(pickable))
                        extraPickable.Interact(__instance, hold, alt);
                }
            }
        }
    }
}
