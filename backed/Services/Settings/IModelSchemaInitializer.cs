namespace AiAgent.Backend.Services.Settings;

/// <summary>
/// 模型与知识库相关表结构初始化服务。
/// </summary>
public interface IModelSchemaInitializer
{
    /// <summary>
    /// 初始化 CodeFirst 表结构、索引和种子数据。
    /// </summary>
    void Initialize();
}