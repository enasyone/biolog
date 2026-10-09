import { PersonListView } from '../../features/identity/components/person-list-view';

export default function StudentsPage() {
  return (
    <PersonListView
      title="Students"
      description="Student roster, filtered from the People directory."
      category="student"
      requiredPermission="nav:students"
    />
  );
}
