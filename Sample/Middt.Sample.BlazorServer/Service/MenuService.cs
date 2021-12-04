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
        new MenuItem() {
            Name="Category",
            Path ="/Bike/Category",
        },
                new MenuItem() {
            Name="Product",
            Path ="  /Bike/Product",
        },
                        new MenuItem() {
            Name="Order",
            Path ="/Bike/Order",
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
            if (MenuItem != null)
            {
                return MenuItem.Title ?? "";
            }

            return "";
        }
    }
}
