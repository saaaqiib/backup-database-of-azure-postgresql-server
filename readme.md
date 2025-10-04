This is atime triggered function that runs everyday 6PM UTC time and creates a dump of the database you want to back up and stores the dump file inside a container in a storage account.

It gets the DB credentials and other variables througgh envionment variables on the function app. 
