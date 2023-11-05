using TeamFive.DataTransfer.Lessons;
using TeamFive.Models;

namespace TeamFive.Services.Lessons;
public interface ILessonService
{
    Task<List<Lesson>> AllLessons();
    Task<LessonDto?> OneLessonAsync(int lessonId, int userId);
    Task<LessonDto?> CreateLessonAsync(Lesson lesson);
    Task<List<LessonWithStartEnd>> AllLessonsForUserIdAsync(int userId);
    Task<LessonNoUsers?> DestroyLessonAsync(int lessonId, int userId);
}
