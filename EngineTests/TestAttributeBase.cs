using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EngineTests
{
    internal abstract class TestAttributeBase : Attribute, ITestDataSource
    {
        public virtual IEnumerable<object?[]> GetData(MethodInfo methodInfo)
        {
            throw new NotImplementedException();
        }

        public virtual string? GetDisplayName(MethodInfo methodInfo, object?[]? data)
        {
            if (data is not null)
            {
                var sb = new StringBuilder();
                foreach (var d in data)
                {
                    sb.AppendFormat("{0}", string.Join(",", d.ToString()));
                }

                return string.Format("{0} ({1})", methodInfo.Name, sb.ToString());
            }

            return methodInfo.Name;
        }
    }
}
