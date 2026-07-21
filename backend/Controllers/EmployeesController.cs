using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MCS_app.Data;
using MCS_app.Models;

namespace MCS_app.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmployeesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly string _documentsBasePath;

        public EmployeesController(AppDbContext context, IWebHostEnvironment environment, IConfiguration configuration)
        {
            _context = context;

            // Physical folder documents are written to/read from. Configurable via
            // "DocumentStorage:BasePath" (absolute path allowed); relative paths
            // resolve against the app's content root.
            var configuredPath = configuration["DocumentStorage:BasePath"] ?? "UploadedFiles";
            _documentsBasePath = Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(environment.ContentRootPath, configuredPath);

            Directory.CreateDirectory(_documentsBasePath);
        }

        // GET: api/employees
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<Employee>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<Employee>>> GetAll()
        {
            var employees = await _context.Employees
                .AsNoTracking()
                .OrderBy(e => e.Id)
                .ToListAsync();

            return Ok(employees);
        }

        // GET: api/employees/5
        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(Employee), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<Employee>> GetById(int id)
        {
            var employee = await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id);

            if (employee is null)
            {
                return NotFound(new { message = $"Employee with id {id} was not found." });
            }

            return Ok(employee);
        }

        // POST: api/employees/5/documents
        [HttpPost("{id:int}/documents")]
        [RequestSizeLimit(20_000_000)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UploadDocument(int id, IFormFile file)
        {
            var employeeExists = await _context.Employees.AnyAsync(e => e.Id == id);
            if (!employeeExists)
            {
                return NotFound(new { message = $"Employee with id {id} was not found." });
            }

            if (file is null || file.Length == 0)
            {
                return BadRequest(new { message = "No file was uploaded." });
            }

            var employeeFolder = Path.Combine(_documentsBasePath, id.ToString());
            Directory.CreateDirectory(employeeFolder);

            // Store under a generated name (not the user-supplied file name) so
            // uploads can never collide or escape the employee's folder.
            var extension = Path.GetExtension(Path.GetFileName(file.FileName));
            var storedFileName = $"{Guid.NewGuid()}{extension}";
            var relativePath = Path.Combine(id.ToString(), storedFileName);

            using (var fileStream = new FileStream(Path.Combine(_documentsBasePath, relativePath), FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            var document = new EmployeeDocument
            {
                EmployeeId = id,
                FileName = file.FileName,
                ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                FilePath = relativePath,
                UploadedAt = DateTime.UtcNow
            };

            _context.EmployeeDocuments.Add(document);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(DownloadDocument), new { id, documentId = document.Id }, new
            {
                id = document.Id,
                fileName = document.FileName,
                uploadedAt = document.UploadedAt
            });
        }

        // GET: api/employees/5/documents
        [HttpGet("{id:int}/documents")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetDocuments(int id)
        {
            var employeeExists = await _context.Employees.AnyAsync(e => e.Id == id);
            if (!employeeExists)
            {
                return NotFound(new { message = $"Employee with id {id} was not found." });
            }

            var documents = await _context.EmployeeDocuments
                .AsNoTracking()
                .Where(d => d.EmployeeId == id)
                .OrderByDescending(d => d.UploadedAt)
                .Select(d => new
                {
                    id = d.Id,
                    fileName = d.FileName,
                    contentType = d.ContentType,
                    uploadedAt = d.UploadedAt
                })
                .ToListAsync();

            return Ok(documents);
        }

        // GET: api/employees/5/documents/3
        [HttpGet("{id:int}/documents/{documentId:int}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DownloadDocument(int id, int documentId)
        {
            var document = await _context.EmployeeDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == documentId && d.EmployeeId == id);

            if (document is null)
            {
                return NotFound(new { message = $"Document with id {documentId} was not found for employee {id}." });
            }

            var fullPath = Path.Combine(_documentsBasePath, document.FilePath);
            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound(new { message = "The file for this document could not be found on disk." });
            }

            return PhysicalFile(fullPath, document.ContentType, document.FileName);
        }
    }
}
