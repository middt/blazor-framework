using System;
using System.Collections.Generic;

namespace Middt.Framework.Common.Configuration
{
    public interface IBaseConfiguration
    {
        TClass Get<TClass>();
    }
}
