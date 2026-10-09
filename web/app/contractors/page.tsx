import { PersonListView } from '../../features/identity/components/person-list-view';

export default function ContractorsPage() {
  return (
    <PersonListView
      title="Contractors"
      description="Contractor roster, filtered from the People directory."
      category="contractor"
      requiredPermission="nav:contractors"
    />
  );
}
