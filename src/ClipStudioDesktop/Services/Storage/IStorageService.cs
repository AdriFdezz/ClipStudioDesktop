namespace ClipStudioDesktop.Services.Storage
{
    public interface IStorageService
    {
        string GetAudioFolder();
        string GetVideoFolder();
        string GetImageFolder();
        void EnsureDirectoriesExist();
    }
}
