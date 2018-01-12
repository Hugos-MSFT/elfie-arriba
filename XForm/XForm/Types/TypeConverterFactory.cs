﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

using Microsoft.CodeAnalysis.Elfie.Model.Strings;

using XForm.Data;

namespace XForm.Types
{
    public enum ValueKinds : byte
    {
        None = 0,
        Invalid = 1,
        InvalidOrNull = 2,
        
        ErrorOnDefault = None,
        ChangeToDefaultOnDefault = None
    }

    public delegate bool[] NegatedTryConvert(DataBatch values, out Array result);

    public static class TypeConverterFactory
    {
        public static Func<DataBatch, DataBatch> GetConverter(Type sourceType, Type targetType, ValueKinds errorOn = ValueKinds.ErrorOnDefault, object defaultValue = null, ValueKinds changeToDefault = ValueKinds.ChangeToDefaultOnDefault)
        {
            Func<DataBatch, DataBatch> converter = TryGetConverter(sourceType, targetType, errorOn, defaultValue, changeToDefault);
            if (converter == null) throw new ArgumentException($"No converter available from {sourceType.Name} to {targetType.Name}.");
            return converter;
        }

        public static Func<DataBatch, DataBatch> TryGetConverter(Type sourceType, Type targetType, ValueKinds errorOn = ValueKinds.ErrorOnDefault, object defaultValue = null, ValueKinds changeToDefault = ValueKinds.ChangeToDefaultOnDefault)
        {
            // Error if there's a default but nothing will be changed to it
            if (defaultValue != null && changeToDefault == ValueKinds.None) throw new ArgumentException("Cast with a default value must have [ChangeToDefaultOn] not 'None'.");

            // Convert the defaultValue to the right type
            defaultValue = ConvertSingle(defaultValue, targetType);

            Func<DataBatch, DataBatch> converter = null;

            // See if the target type provides conversion
            ITypeProvider targetTypeProvider = TypeProviderFactory.TryGet(targetType);
            if (targetTypeProvider != null)
            {
                converter = NegatedTryConvertToConverter(targetTypeProvider.TryGetNegatedTryConvert(sourceType, targetType, defaultValue), "", errorOn, changeToDefault);
                if (converter != null) return converter;
            }

            // See if the source type provides conversion
            ITypeProvider sourceTypeProvider = TypeProviderFactory.TryGet(sourceType);
            if (sourceTypeProvider != null)
            {
                converter = NegatedTryConvertToConverter(sourceTypeProvider.TryGetNegatedTryConvert(sourceType, targetType, defaultValue), "", errorOn, changeToDefault);
                if (converter != null) return converter;
            }

            // Try again with implicit string to String8 conversion
            if (sourceType == typeof(string))
            {
                converter = TryGetConverter(typeof(String8), targetType, errorOn, defaultValue, changeToDefault);

                // If found, encode the string to String8 conversion and then the String8 to target conversion
                if (converter != null)
                {
                    Func<DataBatch, DataBatch> innerConverter = GetConverter(typeof(string), typeof(String8), errorOn, defaultValue, changeToDefault);
                    return (batch) => converter(innerConverter(batch));
                }
            }

            return null;
        }

        public static object ConvertSingle(object value, Type targetType)
        {
            object result;
            if (!TryConvertSingle(value, targetType, out result))
            {
                throw new ArgumentException($"Could not convert \"{value}\" to {targetType.Name}.");
            }
            return result;
        }

        public static bool TryConvertSingle(object value, Type targetType, out object result)
        {
            // Nulls are always converted to null
            if (value == null)
            {
                result = null;
                return true;
            }

            // If the type is already right, just return it
            Type sourceType = value.GetType();
            if (sourceType.Equals(targetType))
            {
                result = value;
                return true;
            }

            // Get the converter for the desired type combination
            Func<DataBatch, DataBatch> converter = GetConverter(sourceType, targetType);

            Array array = null;
            Allocator.AllocateToSize(ref array, 1, sourceType);
            array.SetValue(value, 0);

            DataBatch resultBatch = converter(DataBatch.Single(array, 1));

            // Verify the result was not null unless the input was "" or 'null'
            if (resultBatch.IsNull != null && resultBatch.IsNull[0] == true)
            {
                result = null;

                string stringValue = value.ToString();
                if (stringValue != "" || String.Compare(stringValue, "null", true) == 0) return true;
                return false;
            }

            result = resultBatch.Array.GetValue(0);
            return true;
        }

        public static Func<DataBatch, DataBatch> NegatedTryConvertToConverter(NegatedTryConvert negatedTryConvert, string errorContextMessage, ValueKinds errorOn = ValueKinds.ErrorOnDefault, ValueKinds changeToDefault = ValueKinds.ChangeToDefaultOnDefault)
        {
            if (negatedTryConvert == null) return null;

            Array result;
            bool[] couldNotConvert;

            if (changeToDefault == ValueKinds.None)
            {
                // Invalid/Null/Empty -> Null, so couldNotConvert becomes IsNull
                return (values) =>
                {
                    couldNotConvert = negatedTryConvert(values, out result);
                    ErrorWhenSpecified(errorOn, values, couldNotConvert, errorContextMessage);
                    return DataBatch.All(result, values.Count, couldNotConvert);
                };
            }
            else if (changeToDefault == ValueKinds.Invalid)
            {
                // Invalid -> Default, so keep nulls from source
                return (values) =>
                {
                    couldNotConvert = negatedTryConvert(values, out result);
                    ErrorWhenSpecified(errorOn, values, couldNotConvert, errorContextMessage);
                    return DataBatch.All(result, values.Count, DataBatch.RemapNulls(values, ref couldNotConvert));
                };
            }
            else if (changeToDefault == ValueKinds.InvalidOrNull)
            {
                // Invalid/Null/Empty -> Default, so negate all nulls
                return (values) =>
                {
                    couldNotConvert = negatedTryConvert(values, out result);
                    ErrorWhenSpecified(errorOn, values, couldNotConvert, errorContextMessage);
                    return DataBatch.All(result, values.Count, null);
                };
            }
            else
            {
                throw new NotImplementedException(changeToDefault.ToString());
            }
        }

        private static void ErrorWhenSpecified(ValueKinds errorOnKinds, DataBatch source, bool[] couldNotConvert, string errorContextMessage)
        {
            // If not erroring on anything, nothing to check
            if (errorOnKinds == ValueKinds.None) return;

            // TODO: Use table failure logs. Log should track absolute row count to improve message because this is an IColumn and can't tell.
            if (errorOnKinds == ValueKinds.InvalidOrNull)
            {
                if (couldNotConvert != null) throw new InvalidOperationException($"{errorContextMessage} failed for at least one value.");
            }
            else if (errorOnKinds == ValueKinds.Invalid)
            {
                if (couldNotConvert != null)
                {
                    if (source.IsNull != null)
                    {
                        for (int i = 0; i < source.Count; ++i)
                        {
                            if (couldNotConvert[i] == true && source.IsNull[source.Index(i)] == false) throw new InvalidOperationException($"{errorContextMessage} failed for at least one value.");
                        }
                    }
                }
            }
            else
            {
                throw new NotImplementedException(errorOnKinds.ToString());
            }
        }
    }
}
