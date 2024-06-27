﻿using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using HydrateOrDiedrate.Configuration;
using System;
using Vintagestory.GameContent;

namespace HydrateOrDiedrate.EntityBehavior
{
    public class EntityBehaviorBodyTemperatureHot : Vintagestory.API.Common.Entities.EntityBehavior
    {
        private readonly Config _config;
        private float _currentCooling;
        private float slowaccum;
        private float coolingCounter;
        private BlockPos plrpos;
        private bool inEnclosedRoom;
        private float nearHeatSourceStrength;
        private IWorldAccessor world;
        private Vec3d tmpPos = new Vec3d();

        public EntityBehaviorBodyTemperatureHot(Entity entity) : base(entity)
        {
            _config = new Config();
            _currentCooling = 0;
            LoadCooling();
            InitializeFields();
        }

        public EntityBehaviorBodyTemperatureHot(Entity entity, Config config) : base(entity)
        {
            _config = config;
            _currentCooling = 0;
            LoadCooling();
            InitializeFields();
        }

        private void InitializeFields()
        {
            slowaccum = 0f;
            coolingCounter = 0f;
            plrpos = entity.Pos.AsBlockPos.Copy();
            inEnclosedRoom = false;
            nearHeatSourceStrength = 0f;
            world = entity.World;
        }

        public float CurrentCooling
        {
            get => _currentCooling;
            set
            {
                _currentCooling = GameMath.Clamp(value, 0, float.MaxValue);
                entity.WatchedAttributes.SetFloat("currentCoolingHot", _currentCooling);
                entity.WatchedAttributes.MarkPathDirty("currentCoolingHot");
            }
        }

        public override void OnGameTick(float deltaTime)
        {
            if (!entity.Alive || !_config.HarshHeat) return;
            
            coolingCounter += deltaTime;
            if (coolingCounter > 10f)
            {
                UpdateCoolingFactor();
                coolingCounter = 0f;
            }

            slowaccum += deltaTime;
            if (slowaccum > 3f)
            {
                CheckRoom();
                slowaccum = 0f;
            }
        }

        private void UpdateCoolingFactor()
        {
            float coolingFactor = 0f;
            var entityAgent = entity as EntityAgent;
            if (entityAgent == null || entityAgent.GearInventory == null) return;

            int unequippedSlots = 0;

            for (int i = 0; i < entityAgent.GearInventory.Count; i++)
            {
                if (i == 0 || i == 6 || i == 7 || i == 8 || i == 9 || i == 10)
                {
                    continue;
                }

                var slot = entityAgent.GearInventory[i];

                if (slot?.Itemstack == null)
                {
                    unequippedSlots++;
                }
                else
                {
                    var cooling = CustomItemWearableExtensions.GetCooling(slot, entity.World.Api);
                    if (cooling != 0)
                    {
                        coolingFactor += cooling;
                    }
                }
            }
            
            coolingFactor += unequippedSlots;

            if (entity.WatchedAttributes.GetFloat("wetness", 0f) > 0)
            {
                coolingFactor *= 1.5f;
            }

            if (inEnclosedRoom)
            {
                coolingFactor *= 1.5f;
            }

            coolingFactor -= nearHeatSourceStrength * 0.5f;

            BlockPos entityPos = entity.SidedPos.AsBlockPos;
            int sunlightLevel = world.BlockAccessor.GetLightLevel(entityPos, EnumLightLevelType.TimeOfDaySunLight);
            double hourOfDay = world.Calendar?.HourOfDay ?? 0;

            float sunlightCooling = (16 - sunlightLevel) / 16f; 
            double distanceTo4AM = GameMath.SmoothStep(Math.Abs(GameMath.CyclicValueDistance(4.0, hourOfDay, 24.0) / 12.0));
            double distanceTo3PM = GameMath.SmoothStep(Math.Abs(GameMath.CyclicValueDistance(15.0, hourOfDay, 24.0) / 12.0));
            double diurnalVariationAmplitude = 18f;
            double diurnalCooling = (0.5 - distanceTo4AM) * diurnalVariationAmplitude;
            coolingFactor += (float)(sunlightCooling + diurnalCooling);
            CurrentCooling = Math.Max(0, coolingFactor);
        }

        private void CheckRoom()
        {
            if (entity.Api.Side != EnumAppSide.Server) return;

            plrpos.Set((int)entity.Pos.X, (int)entity.Pos.Y, (int)entity.Pos.Z);
            plrpos.SetDimension(entity.Pos.AsBlockPos.dimension);
            Room room = entity.Api.ModLoader.GetModSystem<RoomRegistry>()?.GetRoomForPosition(plrpos);

            inEnclosedRoom = (room != null && (room.ExitCount == 0 || room.SkylightCount < room.NonSkylightCount));
            nearHeatSourceStrength = 0f;

            double px = entity.Pos.X;
            double py = entity.Pos.Y + 0.9;
            double pz = entity.Pos.Z;
            double proximityPower = inEnclosedRoom ? 0.875 : 1.25;

            BlockPos min, max;
            if (inEnclosedRoom && room.Location.SizeX >= 1 && room.Location.SizeY >= 1 && room.Location.SizeZ >= 1)
            {
                min = new BlockPos(room.Location.MinX, room.Location.MinY, room.Location.MinZ, plrpos.dimension);
                max = new BlockPos(room.Location.MaxX, room.Location.MaxY, room.Location.MaxZ, plrpos.dimension);
            }
            else
            {
                min = plrpos.AddCopy(-3, -3, -3);
                max = plrpos.AddCopy(3, 3, 3);
            }

            world.BlockAccessor.WalkBlocks(min, max, (block, x, y, z) =>
            {
                var blockEntity = world.BlockAccessor.GetBlockEntity(new BlockPos(x, y, z, plrpos.dimension));
                if (blockEntity is Vintagestory.GameContent.IHeatSource heatSource)
                {
                    tmpPos.Set(x, y, z);
                    float factor = Math.Min(1f, 9f / (8f + (float)Math.Pow(tmpPos.DistanceTo(px, py, pz), proximityPower)));
                    nearHeatSourceStrength += heatSource.GetHeatStrength(world, new BlockPos(x, y, z, plrpos.dimension), plrpos) * factor;
                }
            });

            entity.WatchedAttributes.MarkPathDirty("bodyTemp");
        }

        public void LoadCooling()
        {
            _currentCooling = entity.WatchedAttributes.GetFloat("currentCoolingHot", 0);
        }

        public override string PropertyName() => "bodytemperaturehot";
    }
}
