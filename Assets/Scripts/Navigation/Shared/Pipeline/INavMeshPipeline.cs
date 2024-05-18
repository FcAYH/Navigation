using Navigation.PipelineData;

namespace Navigation.Pipeline
{
    public interface INavMeshPipeline
    {
        void Process(DataSet data);
    }
}