using System.Windows.Controls;

namespace Seacore.Resources.Usercontrols
{
    public static class UserControlCache
    {
        private static readonly Dictionary<Type, UserControl> cache = [];

        public static T GetOrCreate<T>(Func<T> factoryMethod) where T : UserControl
        {
            var type = typeof(T);
            if (!cache.TryGetValue(type, out var control))
            {
                control = factoryMethod();
                cache[type] = control;
            }
            return (T)control;
        }
    }
}