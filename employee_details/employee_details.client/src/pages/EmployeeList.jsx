import { useEffect, useState } from "react";
import Header from "../components/Header";
import {
    getEmployees,
    getEmployeeById,
    getEmployeeDocuments,
    uploadDocument,
    downloadDocument
} from "../services/employeeService";

function EmployeeList() {

    const [employees, setEmployees] = useState([]);
    const [selectedEmployee, setSelectedEmployee] = useState(null);
    const [showModal, setShowModal] = useState(false);

    const [documents, setDocuments] = useState([]);
    const [selectedFile, setSelectedFile] = useState(null);

    useEffect(() => {
        loadEmployees();
    }, []);

    async function loadEmployees() {
        try {
            const data = await getEmployees();
            setEmployees(data);
        } catch (error) {
            console.error("Error loading employees:", error);
        }
    }
    useEffect(() => {
        loadEmployees();
    }, []);


    async function loadDocuments(employeeId) {
        try {
            const docs = await getEmployeeDocuments(employeeId);
            setDocuments(docs);
        } catch (error) {
            console.error(error);
            setDocuments([]);
        }
    }

    async function showEmployeeDetails(id) {
        try {
            const employee = await getEmployeeById(id);

            setSelectedEmployee(employee);

            await loadDocuments(id);

            setShowModal(true);

        } catch (error) {
            console.error(error);
            alert("Failed to load employee.");
        }
    }

    async function handleUpload() {

        if (!selectedFile) {
            alert("Please choose a file.");
            return;
        }

        try {

            await uploadDocument(selectedEmployee.id, selectedFile);

            alert("Document uploaded successfully.");

            setSelectedFile(null);

            document.getElementById("fileInput").value = "";

            await loadDocuments(selectedEmployee.id);

        } catch (error) {

            console.error(error);

            alert("Upload failed.");

        }

    }

    return (
        <div className="container py-5">

            <Header />

            <div className="table-responsive shadow rounded">

                <table className="table table-hover table-bordered">

                    <thead className="table-dark">

                        <tr>

                            <th>ID</th>
                            <th>First Name</th>
                            <th>Last Name</th>
                            <th>Email</th>
                            <th>Department</th>
                            <th>Job Title</th>
                            <th>Salary</th>
                            <th>Hire Date</th>

                        </tr>

                    </thead>

                    <tbody>

                        {employees.length > 0 ? (

                            employees.map(employee => (

                                <tr
                                    key={employee.id}
                                    style={{ cursor: "pointer" }}
                                    onClick={() => showEmployeeDetails(employee.id)}
                                >

                                    <td>{employee.id}</td>
                                    <td>{employee.firstName}</td>
                                    <td>{employee.lastName}</td>
                                    <td>{employee.email}</td>
                                    <td>{employee.department}</td>
                                    <td>{employee.jobTitle}</td>
                                    <td>${employee.salary.toLocaleString()}</td>
                                    <td>
                                        {new Date(employee.hireDate).toLocaleDateString()}
                                    </td>

                                </tr>

                            ))

                        ) : (

                            <tr>

                                <td
                                    colSpan="8"
                                    className="text-center py-4"
                                >
                                    No employees found.
                                </td>

                            </tr>

                        )}

                    </tbody>

                </table>

            </div>

            {showModal && selectedEmployee && (

                <div
                    className="modal d-block"
                    style={{ backgroundColor: "rgba(0,0,0,.5)" }}
                >

                    <div className="modal-dialog modal-xl">

                        <div className="modal-content">

                            <div className="modal-header">

                                <h4 className="modal-title">
                                    Employee Details
                                </h4>

                                <button
                                    className="btn-close"
                                    onClick={() => setShowModal(false)}
                                ></button>

                            </div>

                            <div className="modal-body">

                                <div className="row">

                                    <div className="col-md-6">

                                        <p><strong>ID:</strong> {selectedEmployee.id}</p>

                                        <p><strong>First Name:</strong> {selectedEmployee.firstName}</p>

                                        <p><strong>Last Name:</strong> {selectedEmployee.lastName}</p>

                                        <p><strong>Email:</strong> {selectedEmployee.email}</p>

                                    </div>

                                    <div className="col-md-6">

                                        <p><strong>Department:</strong> {selectedEmployee.department}</p>

                                        <p><strong>Job Title:</strong> {selectedEmployee.jobTitle}</p>

                                        <p><strong>Salary:</strong> ${selectedEmployee.salary.toLocaleString()}</p>

                                        <p><strong>Hire Date:</strong> {new Date(selectedEmployee.hireDate).toLocaleDateString()}</p>

                                    </div>

                                </div>

                                <hr />

                                <h4 className="mb-3">
                                    Employee Documents
                                </h4>

                                <div className="input-group mb-4">

                                    <input
                                        id="fileInput"
                                        type="file"
                                        className="form-control"
                                        onChange={(e) =>
                                            setSelectedFile(e.target.files[0])
                                        }
                                    />

                                    <button
                                        className="btn btn-success"
                                        onClick={handleUpload}
                                    >
                                        Upload
                                    </button>

                                </div>
                                <table className="table table-striped table-bordered">

                                    <thead className="table-light">

                                        <tr>

                                            <th>ID</th>
                                            <th>File Name</th>
                                            <th>Content Type</th>
                                            <th>Uploaded At</th>
                                            <th width="120">Action</th>

                                        </tr>

                                    </thead>

                                    <tbody>

                                        {documents.length > 0 ? (

                                            documents.map(doc => (

                                                <tr key={doc.id}>

                                                    <td>{doc.id}</td>

                                                    <td>{doc.fileName}</td>

                                                    <td>{doc.contentType}</td>

                                                    <td>
                                                        {new Date(doc.uploadedAt).toLocaleString()}
                                                    </td>

                                                    <td>

                                                        <button
                                                            className="btn btn-primary btn-sm"
                                                            onClick={() =>
                                                                downloadDocument(
                                                                    selectedEmployee.id,
                                                                    doc.id,
                                                                    doc.fileName
                                                                )
                                                            }
                                                        >
                                                            Download
                                                        </button>

                                                    </td>

                                                </tr>

                                            ))

                                        ) : (

                                            <tr>

                                                <td
                                                    colSpan="5"
                                                    className="text-center"
                                                >
                                                    No Documents Found
                                                </td>

                                            </tr>

                                        )}

                                    </tbody>

                                </table>

                            </div>

                            <div className="modal-footer">

                                <button
                                    className="btn btn-secondary"
                                    onClick={() => {
                                        setShowModal(false);
                                        setDocuments([]);
                                        setSelectedFile(null);
                                    }}
                                >
                                    Close
                                </button>

                            </div>

                        </div>

                    </div>

                </div>

            )}

        </div>

    );
}

export default EmployeeList;