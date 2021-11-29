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
        }
        ,new MenuItem()
        {
            Name = "List Sample Page",
            Path = "ListSamplePage",
            Icon = "&#xe88a"
        }
        , new MenuItem()
        {
            Name = "List Crud Sample Page",
            Path = "ListCrudSamplePage",
            Icon = "&#xe88a"
        }
        ,new MenuItem()
        {
            Name = "Radzen List Sample Page",
            Path = "ListRadzenSamplePage",
            Icon = "&#xe88a"
        }
        , new MenuItem()
        {
            Name = "Radzen List Crud Sample Page",
            Path = "ListRadzenCrudSamplePage",
            Icon = "&#xe88a"
        }
    };

        public IEnumerable<MenuItem> MenuItems
        {
            get
            {
                return allMenuItems;
            }
        }

        public IEnumerable<MenuItem> Filter(string term)
        {
            Func<string, bool> contains = value => value.Contains(term, StringComparison.OrdinalIgnoreCase);

            Func<MenuItem, bool> filter = (MenuItem) => contains(MenuItem.Name) || (MenuItem.Tags != null && MenuItem.Tags.Any(contains));

            return MenuItems.Where(category => category.Children != null && category.Children.Any(filter))
                           .Select(category => new MenuItem()
                           {
                               Name = category.Name,
                               Expanded = true,
                               Children = category.Children.Where(filter).ToArray()
                           }).ToList();
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
