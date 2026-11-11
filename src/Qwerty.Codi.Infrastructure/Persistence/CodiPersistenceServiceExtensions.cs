using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Infrastructure.Persistence.Repositories;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;

namespace Qwerty.Codi.Infrastructure.Persistence;

public static class CodiPersistenceServiceExtensions
{
    /// <summary>Repository interni scoped; la factory singleton crea gli scope e li possiede.</summary>
    public static IServiceCollection AddCodiPersistence(this IServiceCollection services)
    {
        RepoDbSqliteBootstrap.EnsureInitialized();
        services.AddScoped<DbContext>();
        services.AddScoped<IAdminUserRepository, AdminUserRepository>();
        services.AddScoped<ICredentialSessionRepository, CredentialSessionRepository>();
        services.AddScoped<ISchoolRepository, SchoolRepository>();
        services.AddScoped<IClassroomRepository, ClassroomRepository>();
        services.AddScoped<ICourseRepository, CourseRepository>();
        services.AddScoped<ILessonRepository, LessonRepository>();
        services.AddScoped<IExerciseRepository, ExerciseRepository>();
        services.AddScoped<ILessonExerciseRepository, LessonExerciseRepository>();
        services.AddScoped<ILessonSessionRepository, LessonSessionRepository>();
        services.AddScoped<IExerciseDraftRepository, ExerciseDraftRepository>();
        services.AddScoped<IExerciseVersionRepository, ExerciseVersionRepository>();
        services.AddScoped<IExerciseVersionPrivateRepository, ExerciseVersionPrivateRepository>();
        services.AddScoped<IStationRepository, StationRepository>();
        services.AddScoped<ISessionParticipantRepository, SessionParticipantRepository>();
        services.AddScoped<IParticipantStateRepository, ParticipantStateRepository>();
        services.AddScoped<IIdempotencyRecordRepository, IdempotencyRecordRepository>();
        services.AddScoped<ISessionCommandRepository, SessionCommandRepository>();
        services.AddScoped<ICommandDeliveryRepository, CommandDeliveryRepository>();
        services.AddScoped<IOutboxMessageRepository, OutboxMessageRepository>();
        services.AddScoped<IActivityAssignmentRepository, ActivityAssignmentRepository>();
        services.AddScoped<IExerciseAttemptRepository, ExerciseAttemptRepository>();
        services.AddScoped<IAttemptFinalizationGrantRepository, AttemptFinalizationGrantRepository>();
        services.AddScoped<ISessionRemovalStore, SessionRemovalStore>();
        services.AddSingleton<IUnitOfWorkContext, UnitOfWorkContext>();
        return services;
    }
}
