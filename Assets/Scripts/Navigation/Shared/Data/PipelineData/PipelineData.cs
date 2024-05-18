using System.IO;
using Logger = Navigation.Utilities.Logger;

namespace Navigation.PipelineData
{
    public class DataSet
    {
        public NavMeshPreference NavMeshPreference;
        public SolidHeightField SolidHeightField;
        public CompactHeightField CompactHeightField;
        public int RegionCount;
        public ContourSet ContourSet;
        public PolyMeshField PolyMeshField;
        public TriangleMesh TriangleMesh;
        public Tile CurrentTile;
        public Logger Logger;

        private string _workingDirectory;

        public DataSet(string path, Logger logger)
        {
            NavMeshPreference = new NavMeshPreference();
            SolidHeightField = new SolidHeightField();
            CompactHeightField = new CompactHeightField();
            ContourSet = new ContourSet();
            PolyMeshField = new PolyMeshField();
            TriangleMesh = new TriangleMesh();
            Logger = logger;

            _workingDirectory = path;
        }

        public void ClearRuntimeData()
        {
            // TODO:
        }

        public void PersistData(DataType type)
        {
            // 计算路径
            string path = Path.Combine(_workingDirectory, NavMeshPreference.AgentName, type.ToString());
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            switch (type)
            {
                case DataType.SolidHeightField:
                    SolidHeightField.PersistData(path);
                    break;
                case DataType.CompactHeightField:
                    CompactHeightField.PersistData(path);
                    break;
                case DataType.ContourSet:
                    ContourSet.PersistData(path);
                    break;
                case DataType.PolyMeshField:
                    PolyMeshField.PersistData(path);
                    break;
                case DataType.TriangleMesh:
                    TriangleMesh.PersistData(path);
                    break;
            }
        }

        public enum DataType
        {
            SolidHeightField,
            CompactHeightField,
            ContourSet,
            PolyMeshField,
            TriangleMesh
        }
    }
}
