using Middt.Sample.BlazorServer.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Middt.Sample.BlazorServer.Service
{
    public class MenuService
    {
        MenuItem[] allMenuItems = new[] {
        new MenuItem()
        {
            Name = "Home",
            Path = "/",
            Icon = "&#xe88a"
        },
                new MenuItem()
        {
            Name = "Bike",
            Icon = "&#xe7fd",
            Children = new [] {
            new MenuItem() {
                Name="Category",
                Path ="/Bike/Category",
            },
                      new MenuItem() {
                Name="Order",
                Path ="/Bike/Order",
            }
            }
        }
    };

        public IEnumerable<MenuItem> MenuItems
        {
            get
            {
                return allMenuItems;
            }
        }

        public MenuItem FindCurrent(Uri uri)
        {
            return MenuItems.SelectMany(MenuItem => MenuItem.Children ?? new[] { MenuItem })
                           .FirstOrDefault(MenuItem => MenuItem.Path == uri.AbsolutePath || $"/{MenuItem.Path}" == uri.AbsolutePath);
        }

        public string TitleFor(MenuItem MenuItem)
        {
            if (MenuItem != null && MenuItem.Name != "First Look")
            {
                return MenuItem.Title ?? $"Blazor {MenuItem.Name} | a free UI component by Radzen";
            }

            return "Free Blazor Components | 60+ controls by Radzen";
        }

        public string DescriptionFor(MenuItem MenuItem)
        {
            return MenuItem?.Description ?? "The Radzen Blazor component library provides more than 50 UI controls for building rich ASP.NET Core web applications.";
        }
    }
}
