using System;
using System.Collections.Generic;
using System.Reflection;
using Svelto.DataStructures;
using Svelto.Utilities;

namespace Svelto.ECS
{   
    public interface IEntityView
    {
        int ID { get; }
    }
    
    public interface IEntityStruct:IEntityView
    {
        new int ID { set; }
    }

    public abstract class EntityView : IEntityView
    {
        public int ID { get { return _ID; } }

        internal FasterList<KeyValuePair<Type, Action<EntityView, object>>> EntityViewBlazingFastReflection;
        internal int _ID;
    }

    internal static class EntityView<T> where T: EntityView, new()
    {
        internal static T BuildEntityView(int ID) 
        {
            if (FieldCache<T>.list.Count == 0)
            {
                var type = typeof(T);

                var fields = type.GetFields(BindingFlags.Public |
                                            BindingFlags.Instance);

                for (int i = fields.Length - 1; i >= 0; --i)
                {
                    var field = fields[i];

                    Action<EntityView, object> setter = FastInvoke<EntityView>.MakeSetter(field);

                    FieldCache<T>.list.Add(new KeyValuePair<Type, Action<EntityView, object>>(field.FieldType, setter));
                }
            }

            return new T { _ID = ID, EntityViewBlazingFastReflection = FieldCache<T>.list };
        }

        static class FieldCache<W> where W:T
        {
            internal static readonly FasterList<KeyValuePair<Type, Action<EntityView, object>>> list = new FasterList<KeyValuePair<Type, Action<EntityView, object>>>();
        }
    }
}

