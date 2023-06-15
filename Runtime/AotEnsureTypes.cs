using Newtonsoft.Json.Utilities;
using Newtonsoft.Json.Converters;

namespace ApiClient.Runtime
{

    public class AotEnsureTypes
    {
        public static void EnsureTypes()
        {
            AotHelper.EnsureType<StringEnumConverter>();
        }
    }
}