using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Middt.Sample.Api.config.Helper;
using Middt.Sample.Api.config.Model;

namespace Middt.Sample.Api.Model.Database
{
    public partial class BikeStoresContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlServer(new ConfigurationHelper().Get<DBSettings>().AdvenDB);
            }
        }
    }
}
