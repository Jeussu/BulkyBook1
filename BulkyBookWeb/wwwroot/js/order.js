var dataTable;

$(document).ready(function () {
    const status = new URLSearchParams(window.location.search).get("status") || "all";
    const allowedStatuses = ["inprocess", "shipped", "pending", "approved", "cancelled", "all"];
    loadDataTable(allowedStatuses.includes(status) ? status : "all");
});

function loadDataTable(status) {
    const currencyFormatter = new Intl.NumberFormat('en-US', {
        style: 'currency',
        currency: 'USD'
    });

    dataTable = $('#tblData').DataTable({
        "ajax": {
            "url": "/Admin/Order/GetAll?status=" + status
        },
        "columns": [
            { "data": "id", "width": "5%" },
            { "data": "name", "width": "25%" },
            { "data": "phoneNumber", "width": "15%" },
            { "data": "applicationUser.email", "width": "15%" },
            { "data": "orderStatus", "width": "15%" },
            {
                "data": "orderTotal",
                "render": function (data) {
                    return currencyFormatter.format(data || 0);
                },
                "width": "10%"
            },
            {
                "data": "id",
                "render": function (data) {
                    return `
                        <div class="w-75 btn-group" role="group">
                        <a href="/Admin/Order/Details?orderId=${data}"
                        class="btn btn-primary mx-2"> <i class="bi bi-pencil-square"></i></a>
                        
					</div>
                        `
                },
                "width": "5%"
            }
        ],
        "language": {
            "emptyTable": "No orders match this filter."
        }
    });
}

