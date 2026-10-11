using FluentValidation;
using TicketDeskPro.Application.DTOs.Admin;
using TicketDeskPro.Application.Extensions;
using TicketDeskPro.Application.Interfaces;
using TicketDeskPro.Domain.Entities;
using TicketDeskPro.Domain.Interfaces;
using TicketDeskPro.Shared.Constants;
using TicketDeskPro.Shared.Exceptions;

namespace TicketDeskPro.Application.Services.Admin;

/// <summary>Admin CRUD for categories and tags (US-10.01). Deletes are soft —
/// historical tickets keep their labels.</summary>
public interface ICategoryTagService
{
    Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken ct = default);
    Task<CategoryDto> CreateCategoryAsync(CategoryCreateRequest request, CancellationToken ct = default);
    Task<CategoryDto> UpdateCategoryAsync(Guid id, CategoryUpdateRequest request, CancellationToken ct = default);
    Task DeleteCategoryAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<TagDto>> GetTagsAsync(CancellationToken ct = default);
    Task<TagDto> CreateTagAsync(TagCreateRequest request, CancellationToken ct = default);
    Task DeleteTagAsync(Guid id, CancellationToken ct = default);
}

public class CategoryTagService : ICategoryTagService
{
    private readonly ICategoryRepository _categories;
    private readonly ITagRepository _tags;
    private readonly ICacheService _cache;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<CategoryCreateRequest> _categoryCreateValidator;
    private readonly IValidator<CategoryUpdateRequest> _categoryUpdateValidator;
    private readonly IValidator<TagCreateRequest> _tagCreateValidator;

    public CategoryTagService(
        ICategoryRepository categories, ITagRepository tags, ICacheService cache, IUnitOfWork unitOfWork,
        IValidator<CategoryCreateRequest> categoryCreateValidator,
        IValidator<CategoryUpdateRequest> categoryUpdateValidator,
        IValidator<TagCreateRequest> tagCreateValidator)
    {
        _categories = categories;
        _tags = tags;
        _cache = cache;
        _unitOfWork = unitOfWork;
        _categoryCreateValidator = categoryCreateValidator;
        _categoryUpdateValidator = categoryUpdateValidator;
        _tagCreateValidator = tagCreateValidator;
    }

    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken ct = default) =>
        (await _categories.GetAllAsync(ct)).Select(ToDto).ToList();

    public async Task<CategoryDto> CreateCategoryAsync(CategoryCreateRequest request, CancellationToken ct = default)
    {
        await _categoryCreateValidator.ValidateAndThrowAsync(request, ct);
        var name = request.Name.Trim();
        if (await _categories.NameExistsAsync(name, null, ct))
            throw new ConflictException(ErrorCodes.NameExists, $"A category named '{name}' already exists.");

        var category = new Category { Name = name, Description = request.Description?.Trim(), IsActive = true };
        await _categories.AddAsync(category, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        await _cache.RemoveByPrefixAsync(DashboardService.CachePrefix, ct);
        return ToDto(category);
    }

    public async Task<CategoryDto> UpdateCategoryAsync(Guid id, CategoryUpdateRequest request, CancellationToken ct = default)
    {
        await _categoryUpdateValidator.ValidateAndThrowAsync(request, ct);
        var category = await _categories.GetByIdAsync(id, ct)
            ?? throw new NotFoundException(ErrorCodes.CategoryNotFound, "The requested category was not found.");

        var newName = request.Name.Trim();
        if (await _categories.NameExistsAsync(newName, id, ct))
            throw new ConflictException(ErrorCodes.NameExists, $"A category named '{newName}' already exists.");

        category.Name = newName;
        category.Description = request.Description?.Trim();
        category.IsActive = request.IsActive;
        await _unitOfWork.SaveChangesAsync(ct);
        await _cache.RemoveByPrefixAsync(DashboardService.CachePrefix, ct);
        return ToDto(category);
    }

    public async Task DeleteCategoryAsync(Guid id, CancellationToken ct = default)
    {
        var category = await _categories.GetByIdAsync(id, ct)
            ?? throw new NotFoundException(ErrorCodes.CategoryNotFound, "The requested category was not found.");

        // Deleting the label would strip it from every historical ticket (required FK +
        // global filters hide those tickets) — deactivate instead.
        if (await _categories.IsReferencedByTicketsAsync(id, ct))
            throw new ConflictException(ErrorCodes.ReferencedEntity,
                "This category is used by tickets. Set isActive=false instead of deleting it.");
        _categories.Remove(category);
        await _unitOfWork.SaveChangesAsync(ct);
        await _cache.RemoveByPrefixAsync(DashboardService.CachePrefix, ct);
    }

    public async Task<IReadOnlyList<TagDto>> GetTagsAsync(CancellationToken ct = default) =>
        (await _tags.GetAllAsync(ct)).Select(t => new TagDto(t.Id, t.Name)).ToList();

    public async Task<TagDto> CreateTagAsync(TagCreateRequest request, CancellationToken ct = default)
    {
        await _tagCreateValidator.ValidateAndThrowAsync(request, ct);
        var name = request.Name.Trim().ToLowerInvariant();
        if (await _tags.NameExistsAsync(name, null, ct))
            throw new ConflictException(ErrorCodes.NameExists, $"A tag named '{name}' already exists.");

        var tag = new Tag { Name = name };
        await _tags.AddAsync(tag, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return new TagDto(tag.Id, tag.Name);
    }

    public async Task DeleteTagAsync(Guid id, CancellationToken ct = default)
    {
        var tag = await _tags.GetByIdAsync(id, ct)
            ?? throw new NotFoundException(ErrorCodes.TagNotFound, "The requested tag was not found.");

        if (await _tags.IsReferencedByTicketsAsync(id, ct))
            throw new ConflictException(ErrorCodes.ReferencedEntity,
                "This tag is used by tickets and cannot be deleted.");
        _tags.Remove(tag);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private static CategoryDto ToDto(Category c) => new(c.Id, c.Name, c.Description, c.IsActive);
}
