using System;
using System.Reflection.Emit;
using System.Reflection;
using System.Linq.Expressions;

namespace ConsoleApplication1
{
    internal abstract class TypeGenerator
    {
        protected readonly ModuleBuilder mModuleBuilder;
        protected readonly string mTypeName;
        protected static readonly MethodAttributes GetSetAttributes = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual | MethodAttributes.Final;

        public TypeGenerator(string typeName, ModuleBuilder moduleBuilder)
        {
            mTypeName = typeName;
            mModuleBuilder = moduleBuilder;
        }

        public abstract Type Create();

        public static void AddEmptyObjectConstructorCall(ILGenerator ilGenerator)
        {
            ConstructorInfo mObjectBaseConstructor = typeof(object).GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Call, mObjectBaseConstructor);
        }

        public class PropertyBuilders
        {
            public FieldBuilder BackingField { get; set; }
            public MethodBuilder Getter { get; set; }
            public MethodBuilder Setter { get; set; }
        }

        protected PropertyBuilders AddProperty(TypeBuilder typeBuilder, string memberName, string propertyName, Type propertyType)
        {
            FieldBuilder field = typeBuilder.DefineField(memberName, propertyType, FieldAttributes.Private);

            PropertyBuilder property = typeBuilder.DefineProperty(propertyName, PropertyAttributes.HasDefault, propertyType, Type.EmptyTypes);

            MethodBuilder getter = AddPropertyGetter(typeBuilder, property, field);

            MethodBuilder setter = AddPropertySetter(typeBuilder, property, field);

            return new PropertyBuilders { BackingField = field, Getter = getter, Setter = setter };
        }

        protected MethodBuilder AddPropertyGetter(TypeBuilder typeBuilder, PropertyBuilder property, FieldBuilder backingField)
        {
            MethodBuilder getPropertyMethodBuilder = typeBuilder.DefineMethod("get_" + property.Name, GetSetAttributes, property.PropertyType, Type.EmptyTypes);

            ILGenerator ilGenerator = getPropertyMethodBuilder.GetILGenerator();

            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldfld, backingField);
            ilGenerator.Emit(OpCodes.Ret);

            property.SetGetMethod(getPropertyMethodBuilder);

            return getPropertyMethodBuilder;
        }

        protected MethodBuilder AddPropertySetter(TypeBuilder typeBuilder, PropertyBuilder property, FieldBuilder backingField)
        {
            MethodBuilder setPropertyMethodBuilder = typeBuilder.DefineMethod("set_" + property.Name, GetSetAttributes, null, new Type[] { property.PropertyType });

            ILGenerator ilGenerator = setPropertyMethodBuilder.GetILGenerator();

            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldarg_1);
            ilGenerator.Emit(OpCodes.Stfld, backingField);
            ilGenerator.Emit(OpCodes.Ret);

            property.SetSetMethod(setPropertyMethodBuilder);

            return setPropertyMethodBuilder;
        }

        protected FieldInfo AddInstanceField(TypeBuilder typeBuilder, Type fieldType, string fieldName)
        {
            return typeBuilder.DefineField(fieldName, fieldType, FieldAttributes.Private);
        }

        public static Type[] GetArgumentsTypes(object[] objects)
        {
            return Array.ConvertAll<object, Type>(objects, o => o.GetType());
        }

        public static Type[] GetArgumentsTypes(MethodBase method)
        {
            return GetArgumentsTypes(method.GetParameters());
        }

        public static Type[] GetArgumentsTypes(ParameterInfo[] parameters)
        {
            return Array.ConvertAll<ParameterInfo, Type>(parameters, p => p.ParameterType);
        }

        public static NewExpression GetConstructorInvocationExpression(ConstructorInfo constructorInfo)
        {
            ParameterInfo[] parametersInfo = constructorInfo.GetParameters();

            ParameterExpression[] parameters = new ParameterExpression[parametersInfo.Length];

            for (int i = 0; i < parametersInfo.Length; i++)
            {
                parameters[i] = Expression.Parameter(parametersInfo[i].ParameterType, parametersInfo[i].Name);
            }

            return Expression.New(constructorInfo, parameters);
        }

        /// <summary>
        /// Stores the argument in the given position in given class field.
        /// </summary>
        /// <param name="ilGenerator">The il generator.</param>
        /// <param name="argumentPosition">The 1 based method argument position.</param>
        /// <param name="field">The field where the argument should be stored.</param>
        public void StoreArgumentInClassField(ILGenerator ilGenerator, int argumentPosition, FieldInfo field)
        {
            ilGenerator.Emit(OpCodes.Ldarg_0);
            // can be optimised with Ldarg_S in the future
            ilGenerator.Emit(OpCodes.Ldarg, argumentPosition);
            ilGenerator.Emit(OpCodes.Stfld, field);
        }
    }
}
