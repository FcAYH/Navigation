namespace Navigation.PipelineData
{
    public interface IPipelineData
    {
        void PersistData(string path);
        // 当前版本的C#不支持 abstract static void LoadFromJson(string path);
    }
}
