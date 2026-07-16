const API_URL = "https://localhost:7068/api/Employees";

// ======================
// Employees
// ======================

export async function getEmployees() {
    const response = await fetch(API_URL);

    if (!response.ok) {
        throw new Error("Failed to fetch employees");
    }

    return await response.json();
}

export async function getEmployeeById(id) {
    const response = await fetch(`${API_URL}/${id}`);

    if (!response.ok) {
        throw new Error("Failed to fetch employee");
    }

    return await response.json();
}

// ======================
// Documents
// ======================

export async function getEmployeeDocuments(employeeId) {
    const response = await fetch(
        `${API_URL}/${employeeId}/documents`
    );

    if (!response.ok) {
        throw new Error("Failed to load documents");
    }

    return await response.json();
}

export async function uploadDocument(employeeId, file) {
    const formData = new FormData();
    formData.append("file", file);

    const response = await fetch(
        `${API_URL}/${employeeId}/documents`,
        {
            method: "POST",
            body: formData,
        }
    );

    if (!response.ok) {
        throw new Error("Upload failed");
    }

    return await response.json();
}

export async function downloadDocument(employeeId, documentId, fileName) {
    const response = await fetch(
        `${API_URL}/${employeeId}/documents/${documentId}`
    );

    if (!response.ok) {
        throw new Error("Download failed");
    }

    const blob = await response.blob();

    const url = window.URL.createObjectURL(blob);

    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;

    document.body.appendChild(link);
    link.click();
    link.remove();

    window.URL.revokeObjectURL(url);
}