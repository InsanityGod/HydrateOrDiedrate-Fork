﻿using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace HydrateOrDiedrate.EntityBehavior
{
    public class EntityBehaviorThirst : Vintagestory.API.Common.Entities.EntityBehavior
    {
        private float currentThirst;
        private float maxThirst = 10f;
        private float thirstDepletionRate = 0.01f;

        public float CurrentThirst
        {
            get => currentThirst;
            set
            {
                currentThirst = GameMath.Clamp(value, 0, maxThirst);
                MarkDirty();
            }
        }

        public EntityBehaviorThirst(Entity entity) : base(entity)
        {
            InitializeThirst();
        }

        private void InitializeThirst()
        {
            currentThirst = entity.WatchedAttributes.GetFloat("currentThirst", maxThirst);
            entity.WatchedAttributes.SetFloat("currentThirst", currentThirst);
            entity.WatchedAttributes.SetFloat("maxThirst", maxThirst);

            entity.WatchedAttributes.MarkPathDirty("currentThirst");
            entity.WatchedAttributes.MarkPathDirty("maxThirst");

            entity.World.Api.Logger.Debug($"Thirst initialized: currentThirst={currentThirst}, maxThirst={maxThirst}");
        }

        public override void OnGameTick(float deltaTime)
        {
            if (!entity.Alive) return;

            currentThirst -= thirstDepletionRate * deltaTime;
            currentThirst = GameMath.Clamp(currentThirst, 0, maxThirst);

            if (currentThirst <= 0)
            {
                entity.ReceiveDamage(new DamageSource()
                {
                    Source = EnumDamageSource.Internal,
                    Type = EnumDamageType.Hunger // Using hunger as damage type for thirst
                }, 1f);
            }

            entity.WatchedAttributes.SetFloat("currentThirst", currentThirst);
            entity.WatchedAttributes.SetFloat("maxThirst", maxThirst);

            entity.WatchedAttributes.MarkPathDirty("currentThirst");
            entity.WatchedAttributes.MarkPathDirty("maxThirst");

            entity.World.Api.Logger.Debug($"Thirst updated: currentThirst={currentThirst}");
        }

        private void MarkDirty()
        {
            entity.WatchedAttributes.MarkPathDirty("currentThirst");
            entity.WatchedAttributes.MarkPathDirty("maxThirst");
        }

        public override string PropertyName()
        {
            return "thirst";
        }
    }
}
