﻿using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using HydrateOrDiedrate.Configuration;
using Vintagestory.API.Config;

namespace HydrateOrDiedrate.EntityBehavior
{
    public class EntityBehaviorThirst : Vintagestory.API.Common.Entities.EntityBehavior
    {
        private const float DefaultSpeedOfTime = 60f;
        private const float DefaultCalendarSpeedMul = 0.5f;

        private float _currentThirst;
        private float _customThirstRate;
        private int _customThirstTicks;
        private Config _config;
        private float _currentPenaltyAmount;
        private int _thirstTickCounter;
        private bool _isPenaltyApplied;
        private float thirstCounter;
        private int sprintCounter;
        private long lastMoveMs;

        public float CurrentThirst
        {
            get => _currentThirst;
            set
            {
                _currentThirst = GameMath.Clamp(value, 0, _config.MaxThirst);
                entity.WatchedAttributes.SetFloat("currentThirst", _currentThirst);
                entity.WatchedAttributes.MarkPathDirty("currentThirst");
            }
        }

        public float MaxThirst => _config.MaxThirst;

        public EntityBehaviorThirst(Entity entity) : base(entity)
        {
            _config = ModConfig.ReadConfig<Config>(entity.Api, "HydrateOrDiedrateConfig.json");
            if (_config == null)
            {
                _config = new Config();
            }
            LoadThirst();
            InitializeCounters();
        }

        public EntityBehaviorThirst(Entity entity, Config config) : base(entity)
        {
            _config = config ?? new Config();
            LoadThirst();
            InitializeCounters();
        }

        private void InitializeCounters()
        {
            _thirstTickCounter = 0;
            _isPenaltyApplied = false;
            thirstCounter = 0f;
            sprintCounter = 0;
            lastMoveMs = entity.World.ElapsedMilliseconds;
        }

        public override void OnGameTick(float deltaTime)
        {
            if (entity == null || !entity.Alive) return;

            var player = entity as EntityPlayer;
            if (player?.Player?.WorldData?.CurrentGameMode is EnumGameMode.Creative or EnumGameMode.Spectator or EnumGameMode.Guest)
            {
                return;
            }

            if (entity.GetBehavior<EntityBehaviorThirst>() == null) return;

            thirstCounter += deltaTime;
            if (thirstCounter > 10f)
            {
                HandleThirstDecay(deltaTime);
                thirstCounter = 0f;
                sprintCounter = 0;
            }

            ApplyThirstEffects();
        }

        private void HandleThirstDecay(float deltaTime)
        {
            float thirstDecayRate = _customThirstTicks > 0 ? _customThirstRate : _config.ThirstDecayRate;
            int hydrationLossDelay = (int)Math.Floor(entity.WatchedAttributes.GetFloat("hydrationLossDelay", 0));

            float currentSpeedOfTime = entity.Api.World.Calendar.SpeedOfTime;
            float currentCalendarSpeedMul = entity.Api.World.Calendar.CalendarSpeedMul;
            float multiplierPerGameSec = (currentSpeedOfTime / DefaultSpeedOfTime) *
                                         (currentCalendarSpeedMul / DefaultCalendarSpeedMul);

            if (hydrationLossDelay > 0)
            {
                hydrationLossDelay -= Math.Max(1, (int)Math.Floor(multiplierPerGameSec));
                entity.WatchedAttributes.SetFloat("hydrationLossDelay", hydrationLossDelay);
                return;
            }

            var player = entity as EntityPlayer;
            if (player != null)
            {
                if (player.Controls?.Sprint == true)
                {
                    thirstDecayRate *= _config.SprintThirstMultiplier;
                    sprintCounter++;
                }

                if (player.Controls.TriesToMove || player.Controls.Jump || player.Controls.LeftMouseDown ||
                    player.Controls.RightMouseDown)
                {
                    lastMoveMs = entity.World.ElapsedMilliseconds;
                }

                bool isIdle = entity.World.ElapsedMilliseconds - lastMoveMs > 3000L;
                if (isIdle)
                {
                    thirstDecayRate /= 4f;
                }
            }

            if (_config.HarshHeat)
            {
                var climate = entity.World.BlockAccessor.GetClimateAt(entity.ServerPos.AsBlockPos, EnumGetClimateMode.NowValues);
                if (climate.Temperature > _config.TemperatureThreshold)
                {
                    float thirstIncrease = _config.ThirstIncreasePerDegreeMultiplier *
                                           (float)Math.Exp(0.1f * (climate.Temperature - _config.TemperatureThreshold));
                    thirstDecayRate += thirstIncrease;

                    float coolingFactor = entity.WatchedAttributes.GetFloat("currentCoolingHot", 0f);
                    float coolingEffect = coolingFactor * (1f / (1f + (float)Math.Exp(-0.5f * (climate.Temperature - _config.TemperatureThreshold))));
                    thirstDecayRate -= Math.Min(coolingEffect, thirstDecayRate - _config.ThirstDecayRate);
                    
                    thirstDecayRate = Math.Min(thirstDecayRate, _config.ThirstDecayRateMax);
                }
            }


            if (currentSpeedOfTime > DefaultSpeedOfTime || currentCalendarSpeedMul > DefaultCalendarSpeedMul)
            {
                thirstDecayRate *= multiplierPerGameSec;
            }

            if (_customThirstTicks > 0) _customThirstTicks--;
            ModifyThirst(-thirstDecayRate * deltaTime);
            UpdateThirstRate(thirstDecayRate);
        }

        private void ApplyThirstEffects()
        {
            _thirstTickCounter++;

            if (_thirstTickCounter >= 250)
            {
                if (CurrentThirst <= 0)
                {
                    ApplyDamage();
                    ApplyMovementSpeedPenalty(_config.MaxMovementSpeedPenalty);
                }
                else if (CurrentThirst < _config.MovementSpeedPenaltyThreshold)
                {
                    float penaltyAmount = _config.MaxMovementSpeedPenalty * (1 - (CurrentThirst / _config.MovementSpeedPenaltyThreshold));
                    ApplyMovementSpeedPenalty(penaltyAmount);
                }
                else
                {
                    RemoveMovementSpeedPenalty();
                }

                _thirstTickCounter = 0;
            }
        }

        public void SetInitialThirst()
        {
            CurrentThirst = _config.MaxThirst;
            UpdateThirstAttributes();
            RemoveMovementSpeedPenalty();
        }

        public void ResetThirstOnRespawn()
        {
            CurrentThirst = 0.5f * _config.MaxThirst;
            UpdateThirstAttributes();
            RemoveMovementSpeedPenalty();
            InitializeCounters();
        }

        private void ApplyDamage()
        {
            entity.ReceiveDamage(new DamageSource
            {
                Source = EnumDamageSource.Internal,
                Type = EnumDamageType.Injury
            }, _config.ThirstDamage);
        }

        private void ApplyMovementSpeedPenalty(float penaltyAmount)
        {
            if (_currentPenaltyAmount == penaltyAmount && _isPenaltyApplied) return;

            _currentPenaltyAmount = penaltyAmount;
            UpdateWalkSpeed();
            _isPenaltyApplied = true;
        }

        private void RemoveMovementSpeedPenalty()
        {
            if (_currentPenaltyAmount == 0f && !_isPenaltyApplied) return;

            _currentPenaltyAmount = 0f;
            UpdateWalkSpeed();
            _isPenaltyApplied = false;
        }

        private void UpdateWalkSpeed()
        {
            var liquidEncumbrance = entity.GetBehavior<EntityBehaviorLiquidEncumbrance>();
            if (liquidEncumbrance != null && liquidEncumbrance.IsPenaltyApplied)
            {
                entity.Stats.Set("walkspeed", "thirstPenalty", -_currentPenaltyAmount, true);
                return;
            }

            entity.Stats.Set("walkspeed", "thirstPenalty", -_currentPenaltyAmount, true);
        }

        public void ApplyCustomThirstRate(float rate, int ticks)
        {
            _customThirstRate = rate;
            _customThirstTicks = ticks;
        }

        public void LoadThirst()
        {
            _currentThirst = entity.WatchedAttributes.GetFloat("currentThirst", _config.MaxThirst);
            UpdateThirstAttributes();
        }

        public void UpdateThirstAttributes()
        {
            entity.WatchedAttributes.SetFloat("currentThirst", CurrentThirst);
            entity.WatchedAttributes.SetFloat("maxThirst", _config.MaxThirst);
            entity.WatchedAttributes.SetFloat("normalThirstRate", _config.ThirstDecayRate);
            entity.WatchedAttributes.MarkPathDirty("currentThirst");
            entity.WatchedAttributes.MarkPathDirty("maxThirst");
            entity.WatchedAttributes.MarkPathDirty("normalThirstRate");
        }

        public static void UpdateThirstOnServerTick(IServerPlayer player, float deltaTime, Config config)
        {
            if (player?.Entity == null || !player.Entity.WatchedAttributes.GetBool("isFullyInitialized") || !player.Entity.Alive) return;

            var gameMode = player.WorldData.CurrentGameMode;
            if (gameMode == EnumGameMode.Creative || gameMode == EnumGameMode.Spectator || gameMode == EnumGameMode.Guest) return;

            var thirstBehavior = player.Entity.GetBehavior<EntityBehaviorThirst>();
            if (thirstBehavior != null)
            {
                thirstBehavior.OnGameTick(deltaTime);
            }
            else
            {
                if (TryAddThirstBehavior(player.Entity, config))
                {
                    thirstBehavior = player.Entity.GetBehavior<EntityBehaviorThirst>();
                    thirstBehavior?.OnGameTick(deltaTime);
                }
            }
        }

        public void UpdateThirstRate(float rate)
        {
            entity.WatchedAttributes.SetFloat("thirstRate", rate);
            entity.WatchedAttributes.MarkPathDirty("thirstRate");
        }

        public void ApplyHydrationLossDelay(float hydLossDelay)
        {
            int roundedHydLossDelay = (int)Math.Floor(hydLossDelay);
            entity.WatchedAttributes.SetFloat("hydrationLossDelay", roundedHydLossDelay);
            entity.WatchedAttributes.MarkPathDirty("hydrationLossDelay");
        }

        public void ModifyThirst(float amount, float hydLossDelay = 0)
        {
            CurrentThirst += amount;

            int currentHydrationLossDelay = (int)Math.Floor(entity.WatchedAttributes.GetFloat("hydrationLossDelay", 0));

            if (hydLossDelay > currentHydrationLossDelay)
            {
                ApplyHydrationLossDelay(hydLossDelay);
            }
        }

        private static bool TryAddThirstBehavior(Entity entity, Config config)
        {
            try
            {
                var thirstBehavior = new EntityBehaviorThirst(entity, config);
                entity.AddBehavior(thirstBehavior);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public override string PropertyName() => "thirst";
    }
}
