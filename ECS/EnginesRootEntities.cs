﻿using System;
using System.Collections.Generic;
using Svelto.DataStructures;
using Svelto.ECS.Internal;

#if EXPERIMENTAL
using Svelto.ECS.Experimental;
using Svelto.ECS.Experimental.Internal;
#endif

#if ENGINE_PROFILER_ENABLED && UNITY_EDITOR
using Svelto.ECS.Profiler;
#endif

namespace Svelto.ECS
{
    public partial class EnginesRoot : IDisposable
    {
        /// <summary>
        /// an EnginesRoot reference cannot be held by anything else than the Composition Root
        /// where it has been created. IEntityFactory and IEntityFunctions allow a weakreference
        /// of the EnginesRoot to be passed around.
        /// </summary>
        /// <returns></returns>
        public IEntityFactory GenerateEntityFactory()
        {
            return new GenericEntityFactory(new DataStructures.WeakReference<EnginesRoot>(this));
        }

        public IEntityFunctions GenerateEntityFunctions()
        {
            return new GenericEntityFunctions(new DataStructures.WeakReference<EnginesRoot>(this));
        }

        /// <summary>
        /// The EntityDescriptor doesn't need to be ever instantiated. It just describes the Entity
        /// itself in terms of EntityViews to build. The Implementors are passed to fill the 
        /// references of the EntityViews components. Please read the articles on my blog
        /// to understand better the terminologies
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entityID"></param>
        /// <param name="implementors"></param>
        void BuildEntity<T>(int entityID, object[] implementors = null) where T : IEntityDescriptor, new()
        {
            EntityFactory.BuildEntityViews
                (entityID, _entityViewsToAdd.current, EntityDescriptorTemplate<T>.Default, implementors);
        }

        /// <summary>
        /// When the type of the entity is not known (this is a special case!) an EntityDescriptorInfo
        /// can be built in place of the generic parameter T. 
        /// </summary>
        /// <param name="entityID"></param>
        /// <param name="entityDescriptor"></param>
        /// <param name="implementors"></param>
        void BuildEntity(int entityID, EntityDescriptorInfo entityDescriptor, object[] implementors = null)
        {
            EntityFactory.BuildEntityViews
                (entityID, _entityViewsToAdd.current, entityDescriptor, implementors);
        }

        /// <summary>
        /// A meta entity is a way to manage a set of entitites that are not easily 
        /// queriable otherwise. For example you may want to group existing entities
        /// by size and type and then use the meta entity entityView to manage the data 
        /// shared among the single entities of the same type and size. This will 
        /// prevent the scenario where the coder is forced to parse all the entities to 
        /// find the ones of the same size and type. 
        /// Since the entities are managed through the shared entityView, the same
        /// shared entityView must be found on the single entities of the same type and size.
        /// The shared entityView of the meta entity is then used by engines that are meant 
        /// to manage a group of entities through a single entityView. 
        /// The same engine can manage several meta entities entityViews too.
        /// The Engine manages the logic of the Meta EntityView data and other engines
        /// can read back this data through the normal entity as the shared entityView
        /// will be present in their descriptor too.
        /// It's a way to control a group of Entities through a entityView only.
        /// This set of entities can share exactly the same entityView reference if 
        /// built through this function. In this way, if you need to set a variable
        /// on a group of entities, instead to inject N entityViews and iterate over
        /// them to set the same value, you can inject just one entityView, set the value
        /// and be sure that the value is shared between entities.
        /// </summary>
        /// <param name="metaEntityID"></param>
        /// <param name="ed"></param>
        /// <param name="implementors"></param>
        void BuildMetaEntity<T>(int metaEntityID, object[] implementors) where T : IEntityDescriptor, new()
        {
            EntityFactory.BuildEntityViews(metaEntityID, _entityViewsToAdd.current,
                                           EntityDescriptorTemplate<T>.Default, implementors);
        }

        /// <summary>
        /// Using this function is like building a normal entity, but the entityViews
        /// are grouped by groupID to be more efficently processed inside engines and
        /// improve cache locality. Either class entityViews and struct entityViews can be
        /// grouped.
        /// </summary>
        /// <param name="entityID"></param>
        /// <param name="groupID"></param>
        /// <param name="ed"></param>
        /// <param name="implementors"></param>
        void BuildEntityInGroup<T>(int entityID, int groupID, object[] implementors = null) where T : IEntityDescriptor, new()
        {
            EntityFactory.BuildGroupedEntityViews(entityID, groupID,
                                                  _groupedEntityViewsToAdd.current,
                                                  EntityDescriptorTemplate<T>.Default,
                                                  implementors);
        }

        void BuildEntityInGroup(int entityID, int groupID, EntityDescriptorInfo entityDescriptor, object[] implementors = null)
        {
            EntityFactory.BuildGroupedEntityViews(entityID, groupID,
                                                  _groupedEntityViewsToAdd.current,
                                                  entityDescriptor, implementors);
        }

        void Preallocate<T>(int size) where T : IEntityDescriptor, new()
        {
            var entityViewsToBuild = EntityDescriptorTemplate<T>.Default.entityViewsToBuild;
            int count = entityViewsToBuild.Length;

            for (int index = 0; index < count; index++)
            {
                var entityViewBuilder = entityViewsToBuild[index];
                var entityViewType = entityViewBuilder.GetEntityViewType();

                ITypeSafeList dbList;
                if (_entityViewsDB.TryGetValue(entityViewType, out dbList) == false)
                    _entityViewsDB[entityViewType] = entityViewBuilder.Preallocate(ref dbList, size);
                else
                    dbList.ReserveCapacity(size);

                if (_entityViewsToAdd.current.TryGetValue(entityViewType, out dbList) == false)
                    _entityViewsToAdd.current[entityViewType] = entityViewBuilder.Preallocate(ref dbList, size);
                else
                    dbList.ReserveCapacity(size);
            }
        }

        void RemoveEntity(int entityID, IRemoveEntityComponent removeInfo)
        {
            var removeEntityImplementor = removeInfo as RemoveEntityImplementor;

            if (removeEntityImplementor.isInAGroup)
                InternalRemove(removeEntityImplementor.removeEntityInfo.entityViewsToBuild, entityID, removeEntityImplementor.groupID, _entityViewsDB);
            else
                InternalRemove(removeEntityImplementor.removeEntityInfo.entityViewsToBuild, entityID, _entityViewsDB);
        }

        void RemoveEntity<T>(int entityID) where T : IEntityDescriptor, new()
        {
            InternalRemove(EntityDescriptorTemplate<T>.Default.entityViewsToBuild, entityID, _entityViewsDB);
        }

        void RemoveMetaEntity<T>(int metaEntityID) where T : IEntityDescriptor, new()
        {
            InternalRemove(EntityDescriptorTemplate<T>.Default.entityViewsToBuild, metaEntityID, _metaEntityViewsDB);
        }

        void RemoveEntityFromGroup<T>(int entityID, int groupID) where T : IEntityDescriptor, new()
        {
            InternalRemove(EntityDescriptorTemplate<T>.Default.entityViewsToBuild, entityID, _groupEntityViewsDB[groupID]);
        }

        void InternalRemove(IEntityViewBuilder[] entityViewBuilders, int entityID,
                    Dictionary<Type, ITypeSafeList> entityViewsDB)
        {
            int entityViewBuildersCount = entityViewBuilders.Length;

            for (int i = 0; i < entityViewBuildersCount; i++)
            {
                Type entityViewType = entityViewBuilders[i].GetEntityViewType();

                ITypeSafeList entityViews = entityViewsDB[entityViewType];
                if (entityViews.UnorderedRemove(entityID) == false)
                    entityViewsDB.Remove(entityViewType);

                if (entityViews.isQueryiableEntityView)
                {
                    var typeSafeDictionary = _entityViewsDBdic[entityViewType];
                    var entityView = typeSafeDictionary.GetIndexedEntityView(entityID);

                    if (typeSafeDictionary.Remove(entityID) == false)
                        _entityViewsDBdic.Remove(entityViewType);

                    RemoveEntityViewFromEngines(_entityViewEngines, entityView, entityViewType);
                }
            }
        }

        void InternalRemove(IEntityViewBuilder[] entityViewBuilders, int entityID, int groupID,
                            Dictionary<Type, ITypeSafeList> entityViewsDB)
        {
            int entityViewBuildersCount = entityViewBuilders.Length;

            for (int i = 0; i < entityViewBuildersCount; i++)
            {
                Type entityViewType = entityViewBuilders[i].GetEntityViewType();
                Dictionary<Type, ITypeSafeList> dictionary = _groupEntityViewsDB[groupID];

                if (dictionary[entityViewType].UnorderedRemove(entityID) == false)
                    dictionary.Remove(entityViewType);

                if (dictionary.Count == 0) _groupEntityViewsDB.Remove(groupID);
            }

            InternalRemove(entityViewBuilders, entityID, entityViewsDB);
        }

        static void RemoveEntityViewFromEngines(Dictionary<Type, FasterList<IHandleEntityViewEngine>> entityViewEngines,
                                                IEntityView entityView, Type entityViewType)
        {
            FasterList<IHandleEntityViewEngine> enginesForEntityView;

            if (entityViewEngines.TryGetValue(entityViewType, out enginesForEntityView))
            {
                int count;
                var fastList = FasterList<IHandleEntityViewEngine>.NoVirt.ToArrayFast(enginesForEntityView, out count);

                for (int j = 0; j < count; j++)
                {
#if ENGINE_PROFILER_ENABLED && UNITY_EDITOR
                    EngineProfiler.MonitorRemoveDuration(_removeEntityViewFromEngine, fastList[j], entityView);
#else
                    fastList[j].Remove(entityView);
#endif
                }
            }
        }

#if ENGINE_PROFILER_ENABLED && UNITY_EDITOR
        static void AddEntityViewToEngine(IHandleEntityViewEngine engine, IEntityView entityView)
        {
            engine.Add(entityView);
        }

        static void RemoveEntityViewFromEngine(IHandleEntityViewEngine engine, IEntityView entityView)
        {
            engine.Remove(entityView);
        }
#endif

        class GenericEntityFactory : IEntityFactory
        {
            DataStructures.WeakReference<EnginesRoot> _weakEngine;

            public GenericEntityFactory(DataStructures.WeakReference<EnginesRoot> weakReference)
            {
                _weakEngine = weakReference;
            }

            public void BuildEntity<T>(int entityID, object[] implementors = null) where T : IEntityDescriptor, new()
            {
                _weakEngine.Target.BuildEntity<T>(entityID, implementors);
            }

            public void BuildEntity(int entityID, EntityDescriptorInfo entityDescriptor, object[] implementors = null)
            {
                _weakEngine.Target.BuildEntity(entityID, entityDescriptor, implementors);
            }

            public void BuildMetaEntity<T>(int metaEntityID, object[] implementors = null) where T : IEntityDescriptor, new()
            {
                _weakEngine.Target.BuildMetaEntity<T>(metaEntityID, implementors);
            }

            public void BuildEntityInGroup<T>(int entityID, int groupID, object[] implementors = null) where T : IEntityDescriptor, new()
            {
                _weakEngine.Target.BuildEntityInGroup<T>(entityID, groupID, implementors);
            }

            public void BuildEntityInGroup(int entityID, int groupID, EntityDescriptorInfo entityDescriptor, object[] implementors = null)
            {
                _weakEngine.Target.BuildEntityInGroup(entityID, groupID, entityDescriptor, implementors);
            }

            public void Preallocate<T>(int size) where T : IEntityDescriptor, new()
            {
                _weakEngine.Target.Preallocate<T>(size);
            }
        }

        class GenericEntityFunctions : IEntityFunctions
        {
            public GenericEntityFunctions(DataStructures.WeakReference<EnginesRoot> weakReference)
            {
                _weakReference = weakReference;
            }

            public void RemoveEntity(int entityID, IRemoveEntityComponent removeInfo)
            {
                _weakReference.Target.RemoveEntity(entityID, removeInfo);
            }

            public void RemoveEntity<T>(int entityID) where T : IEntityDescriptor, new()
            {
                _weakReference.Target.RemoveEntity<T>(entityID);
            }

            public void RemoveMetaEntity<T>(int metaEntityID) where T : IEntityDescriptor, new()
            {
                _weakReference.Target.RemoveEntity<T>(metaEntityID);
            }

            public void RemoveEntityFromGroup<T>(int entityID, int groupID) where T : IEntityDescriptor, new()
            {
                _weakReference.Target.RemoveEntity<T>(entityID);
            }

            readonly DataStructures.WeakReference<EnginesRoot> _weakReference;
        }

        public void Dispose()
        {
            foreach (var entity in _entityViewsDB)
            {
                if (entity.Value.isQueryiableEntityView == true)
                {
                    foreach (var entityView in entity.Value)
                    {
                        RemoveEntityViewFromEngines(_entityViewEngines, entityView as EntityView, entity.Key);
                    }
                }
            }

            foreach (var entity in _metaEntityViewsDB)
            {
                foreach (var entityView in entity.Value)
                {
                    RemoveEntityViewFromEngines(_entityViewEngines, entityView as EntityView, entity.Key);
                }
            }
        }
    }
}