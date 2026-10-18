#if WASDK
namespace Uno.Extensions;

public static class PolyfillsExtension {
    extension<T>(ICollection<T> collection)
    {
        public void AddRange(IEnumerable<T> items)
        {
            foreach (var item in items)
                collection.Add(item);
        }
    }
}
#endif