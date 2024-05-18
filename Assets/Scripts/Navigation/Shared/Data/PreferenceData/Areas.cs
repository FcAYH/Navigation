using UnityEngine;
using Sirenix.OdinInspector;
using System.IO;
using Path = Navigation.Utilities.Path;

namespace Navigation.PreferenceData
{
    [CreateAssetMenu(menuName = "Navigation/Generator/Areas")]
    public class Areas : ScriptableObject
    {
        [ReadOnly]
        public Area[] AreaSet;

        public Areas()
        {
            AreaSet = new Area[32];
            AreaSet[0].Name = "NotWalkable";
            AreaSet[0].Priority = 1;
            AreaSet[1].Name = "Walkable";
            AreaSet[1].Priority = 1;
            for (int i = 0; i < 32; i++)
                AreaSet[i].Priority = 1;
        }

        public void GenerateAreaMaskFile()
        {
            var path = Path.AreaMaskFilePath;

            string @namespace = "namespace Navigation";
            string name = "    public enum AreaMask : uint";
            using (var sw = new StreamWriter(path))
            {
                sw.WriteLine(@namespace);
                sw.WriteLine("{");
                sw.WriteLine(name);
                sw.WriteLine("    {");

                for (int i = 0; i < AreaSet.Length; i++)
                {
                    var area = AreaSet[i];
                    var end = (i == AreaSet.Length - 1) ? "" : ",";
                    if (!string.IsNullOrEmpty(area.Name))
                    {
                        uint enumNum = 1u << i;
                        sw.WriteLine($"        {area.Name} = {enumNum}u{end}");
                    }
                }

                sw.WriteLine("    }");
                sw.WriteLine("}");
            }
        }
    }
}