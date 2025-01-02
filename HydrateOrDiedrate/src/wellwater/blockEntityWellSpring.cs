﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace HydrateOrDiedrate.Wellwater
{
    public class BlockEntityWellSpring : BlockEntity
    {
        private ICoreServerAPI sapi;
        private AquiferManager _aquiferManager;
        private int updateIntervalMs = 2000; //600000
        private const int maxWaterLevel = 7;

        public void SetAquiferSystem(AquiferManager aquiferManager)
        {
            this._aquiferManager = aquiferManager;
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            sapi = api as ICoreServerAPI;

            if (sapi != null)
            {
                _aquiferManager = HydrateOrDiedrateModSystem.HydrateOrDiedrateGlobals.AquiferManager;

                if (_aquiferManager == null)
                {
                    return;
                }

                // Calculate initial depth factor
                double maxWorldHeight = sapi.WorldManager.MapSizeY;
                double waterLineY = Math.Round(0.4296875 * maxWorldHeight);
                double depthFactor = (waterLineY - Pos.Y) / (waterLineY - 1);
                if (depthFactor < 0)
                {
                    depthFactor = 0;
                }
                _aquiferManager.RegisterWellspring(Pos, depthFactor);

                RegisterGameTickListener(OnTick, updateIntervalMs);
            }
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();

            if (_aquiferManager != null)
            {
                _aquiferManager.UnregisterWellspring(Pos);
            }
        }
        
        private void OnTick(float dt)
        {
            if (sapi == null || _aquiferManager == null)
            {
                return;
            }

            Vec2i chunkCoord = new Vec2i(Pos.X / sapi.World.BlockAccessor.ChunkSize, Pos.Z / sapi.World.BlockAccessor.ChunkSize);
            var aquiferData = _aquiferManager.GetAquiferData(chunkCoord);

            if (aquiferData == null || aquiferData.AquiferRating == 0)
            {
                return;
            }

            var wellsprings = _aquiferManager.GetWellspringsInChunk(chunkCoord);

            if (wellsprings.Count == 0) return;

            double remainingRating = aquiferData.AquiferRating;
            var sortedWellsprings = wellsprings.OrderByDescending(ws => ws.DepthFactor).ToList();
            Dictionary<BlockPos, double> outputRates = new Dictionary<BlockPos, double>();

            foreach (var wellspring in sortedWellsprings)
            {
                double requiredRating = aquiferData.AquiferRating * wellspring.DepthFactor;

                if (remainingRating >= requiredRating)
                {
                    outputRates[wellspring.Position] = requiredRating;
                    remainingRating -= requiredRating;
                }
                else
                {
                    outputRates[wellspring.Position] = remainingRating;
                    remainingRating = 0;
                }
            }

            if (outputRates.TryGetValue(Pos, out double aquiferOutputRate) && aquiferOutputRate > 0)
            {
                string waterType = aquiferData.IsSalty ? "salt" : "fresh";
                sapi.Logger.Debug($"Generating {waterType} water with output rate {aquiferOutputRate} at {Pos}.");

                TryGenerateWater(waterType, aquiferData);
            }
        }

        public void GenerateWater(string waterType, double aquiferOutputRate, AquiferManager.AquiferData aquiferData)
        {
            var blockAccessor = sapi.World.BlockAccessor;
            BlockPos abovePos = Pos.UpCopy();
            Block aboveBlock = blockAccessor.GetBlock(abovePos, 1);

            int waterLevel = aboveBlock?.LiquidLevel ?? 0;

            if (aboveBlock != null && aboveBlock.Code.Path.Contains("water"))
            {
                if (aquiferData.IsSalty && !aboveBlock.Code.Path.Contains("salt"))
                {
                    ReplaceWithWaterBlock(abovePos, waterType, waterLevel + 1);
                }
                else if (waterLevel < maxWaterLevel)
                {
                    ReplaceWithWaterBlock(abovePos, waterType, waterLevel + 1);
                }
            }
            else
            {
                ReplaceWithWaterBlock(abovePos, waterType, 1);
            }

            HandleMudGeneration(aquiferData);
        }

        private void TryGenerateWater(string waterType, AquiferManager.AquiferData aquiferData)
        {
            var blockAccessor = sapi.World.BlockAccessor;
            BlockPos abovePos = Pos.UpCopy();
            Block aboveBlock = blockAccessor.GetBlock(abovePos, 1);

            int waterLevel = aboveBlock?.LiquidLevel ?? 0;

            if (aboveBlock != null && aboveBlock.Code.Path.Contains("water"))
            {
                if (aquiferData.IsSalty && !aboveBlock.Code.Path.Contains("salt"))
                {
                    ReplaceWithWaterBlock(abovePos, waterType, waterLevel + 1);
                }
                else if (waterLevel < maxWaterLevel)
                {
                    ReplaceWithWaterBlock(abovePos, waterType, waterLevel + 1);
                }
            }
            else
            {
                ReplaceWithWaterBlock(abovePos, waterType, 1);
            }

            HandleMudGeneration(aquiferData);
        }

        private void ReplaceWithWaterBlock(BlockPos pos, string waterType, int level)
        {
            level = Math.Clamp(level, 1, maxWaterLevel);
            string blockCode = $"hydrateordiedrate:wellwater{waterType}-natural-still-{level}";
            Block waterBlock = sapi.World.GetBlock(new AssetLocation(blockCode));
            if (waterBlock != null)
            {
                var blockAccessor = sapi.World.BlockAccessor;
                blockAccessor.SetBlock(waterBlock.BlockId, pos, 1);
                blockAccessor.TriggerNeighbourBlockUpdate(pos);
            }
        }

        private void HandleMudGeneration(AquiferManager.AquiferData aquiferData)
        {
            var blockAccessor = sapi.World.BlockAccessor;
            Block[] adjacentBlocks = new Block[]
            {
                blockAccessor.GetBlock(Pos.NorthCopy()),
                blockAccessor.GetBlock(Pos.EastCopy()),
                blockAccessor.GetBlock(Pos.SouthCopy()),
                blockAccessor.GetBlock(Pos.WestCopy())
            };

            foreach (var block in adjacentBlocks)
            {
                if (block?.Code?.Path.StartsWith("soil-") == true || block.Code.Path.StartsWith("sand-") || block.Code.Path.StartsWith("gravel-"))
                {
                    BlockPos abovePos = Pos.UpCopy();
                    Block aboveBlock = blockAccessor.GetBlock(abovePos, 1); // Layer 1 for fluids

                    if (aquiferData.IsSalty) continue;

                    if (aboveBlock != null && aboveBlock.Code.Path.Contains("fresh"))
                    {
                        ReplaceWithWaterBlock(abovePos, "muddy", aboveBlock.LiquidLevel);
                    }
                    else if (aboveBlock == null)
                    {
                        ReplaceWithWaterBlock(abovePos, "muddy", 1);
                    }
                }
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);

            if (worldForResolving.Api.Side == EnumAppSide.Server)
            {
                _aquiferManager = HydrateOrDiedrateModSystem.HydrateOrDiedrateGlobals.AquiferManager;
                if (_aquiferManager == null)
                {
                    sapi?.Logger.Warning("Aquifer system is not initialized during data load. Functionality may be limited.");
                }
            }
        }


        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine("This block generates water based on the aquifer level beneath it.");
        }
    }
}
