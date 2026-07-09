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

        public EmployeesController(AppDbContext context)
        {
            _context = context;
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

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);

            var document = new EmployeeDocument
            {
                EmployeeId = id,
                FileName = file.FileName,
                ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                Data = memoryStream.ToArray(),
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

            return File(document.Data, document.ContentType, document.FileName);
        }
    }
}
