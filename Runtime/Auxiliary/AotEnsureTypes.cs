using Newtonsoft.Json.Utilities;
using Newtonsoft.Json.Converters;

namespace ApiClient.Runtime.Auxiliary
{
    public class AotEnsureTypes
    {
        public static void EnsureTypes()
        {
            AotHelper.EnsureType<StringEnumConverter>();
        }
    }
}