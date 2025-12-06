namespace ClipStudioDesktop.Services
{
    public interface IStorageService
    {
        string GetAudioFolder();
        string GetVideoFolder();
        string GetImageFolder();
        void EnsureDirectoriesExist();
    }
}
