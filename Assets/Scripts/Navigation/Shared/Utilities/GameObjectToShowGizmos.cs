using System.Reflection;
using OpenCover.Framework.Model;
using UnityEngine;

namespace Navigation.Display
{
    public class GameObjectToShowGizmos : MonoBehaviour
    {
        private MethodInfo _gizmosDrawerMethod;
        private MethodInfo _pathFinderMethod;

        private void Reset()
        {
            var assembly = System.Reflection.Assembly.Load("Assembly-CSharp-Editor");

            // 通过反射，去 Assembly-CSharp-Editor.dll 中找到 GizmosDrawer 类，并调用 OnDrawGizmos 方法
            var type = assembly.GetType("Navigation.Display.GizmosDrawer");
            _gizmosDrawerMethod = type.GetMethod("DrawGizmos");

            // 通过反射，去 Assembly-CSharp-Editor.dll 中找到 PathFinder 类，并调用 OnDrawGizmos 方法
            var pathFinderType = assembly.GetType("Navigation.Finder.Editor.PathFinder");
            _pathFinderMethod = pathFinderType.GetMethod("DrawGizmos");
        }
        private void OnDrawGizmos()
        {
            _gizmosDrawerMethod?.Invoke(null, null);

            _pathFinderMethod?.Invoke(null, null);
        }
    }
}