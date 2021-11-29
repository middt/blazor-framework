using System.Collections.Generic;

namespace Suges.Template.BlazorServer.Model
{
    public class MenuItem
    {
        public string Name { get; set; }
        public string Icon { get; set; }
        public string Path { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool Expanded { get; set; }
        public IEnumerable<MenuItem> Children { get; set; }
        public IEnumerable<string> Tags { get; set; }
    }
}
